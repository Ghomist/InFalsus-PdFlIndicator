extern alias GameData;
using System;
using System.Text;
using BepInEx.Logging;
using ifapp.Game;
using ifapp.Game.Scenes.Game.UI;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using FastText.Data;
using FText = FastText.FastText;
using _cH = GameData::_K._cH;

namespace PdFlIndicator;

/// <summary>
/// 局内顶部指示：状态(PD/FL) + EXACT / NEAR / BREAK 计数。
/// 渲染复用游戏的 FastText：新建独立 GameObject（与 ClearStateContainer 同级，不挂在其下），
/// 只从现有 FastText 拷贝字体族/样式/材质引用，不修改任何游戏对象。
/// </summary>
public sealed class PdFlBehaviour : MonoBehaviour
{
    private const float TickSeconds = 0.1f;

    private static ManualLogSource L => Plugin.Log2;

    private float _nextTick;
    private ClearStateContainer _csc;
    private FText _tm;
    private string _shownText;
    private int _exact;
    private int _exactBig;
    private int _exactSmall;
    private int _near;
    private int _brk;
    private int _lastExact = -1;
    private int _lastNear = -1;
    private int _lastBrk = -1;
    private bool _warned;
    private bool _labelWarned;
    private bool _textWarned;
    private FText _tpl;
    private string _strippedNames;
    private float _diagAt = -1f;
    private Color _curColor = new Color(-1f, 0f, 0f);
    private Color _pdColor, _flColor, _clrColor;
    private bool _colorsResolved;
    private float[] _nearSince;
    private float[] _brkSince;

    public PdFlBehaviour(IntPtr p) : base(p) { }

    public void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextTick) return;
        _nextTick = now + TickSeconds;

        if (!Plugin.CfgEnabled.Value) { ApplyText(null); return; }

        EnsureAnchor();
        if (_csc == null) { ApplyText(null); return; }

        CountJudgements(now);
        ApplyText(BuildText());
        ApplyColor();
        if (_tm != null && (bool)_tm) FollowAnchor();
        if (_diagAt > 0f && now >= _diagAt && Plugin.CfgVerbose.Value) { _diagAt = -1f; Diag(); }

        if (Plugin.CfgShowCounts.Value &&
            (_exact != _lastExact || _near != _lastNear || _brk != _lastBrk))
        {
            _lastExact = _exact;
            _lastNear = _near;
            _lastBrk = _brk;
            L.LogInfo($"[pdfl] exact={_exact} near={_near} break={_brk}");
        }
    }

    // ---------- 文本 ----------

    private string BuildText()
    {
        string state;
        if (_brk > 0) state = "";
        else if (_near > 0) state = Plugin.CfgFlText.Value;
        else state = Plugin.CfgPdText.Value;

        string fmt = Plugin.CfgFormat.Value;
        if (fmt == null) fmt = "";

        var sb = new StringBuilder(fmt.Length + 16);
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c != '$' || i + 1 >= fmt.Length) { sb.Append(c); continue; }
            // 两字符变量优先：$be / $se（避免被 $b / $s 抢先匹配）
            if (i + 2 < fmt.Length && fmt[i + 2] == 'e')
            {
                char p2 = fmt[i + 1];
                if (p2 == 'b') { sb.Append(_exactBig); i += 2; continue; }
                if (p2 == 's') { sb.Append(_exactSmall); i += 2; continue; }
            }
            char v = fmt[i + 1];
            switch (v)
            {
                case '$': sb.Append('$'); i++; break;
                case 's': sb.Append(state); i++; break;
                case 'e': sb.Append(_exact); i++; break;
                case 'n': sb.Append(_near); i++; break;
                case 'b': sb.Append(_brk); i++; break;
                default: sb.Append(c); break;
            }
        }

        string text = sb.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private void ApplyText(string text)
    {
        if (text == _shownText) return;
        _shownText = text;

        try
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_tm != null && (bool)_tm) _tm.gameObject.SetActive(false);
                return;
            }

            EnsureLabel();
            if (_tm == null) return;

            var go = _tm.gameObject;
            if (!go.activeSelf) go.SetActive(true);
            FollowAnchor();
            _tm.SetTextNonLocalized(text);

            if (Plugin.CfgVerbose.Value) L.LogInfo($"[pdfl] text = '{text}' pos={_tm.transform.position}");
        }
        catch (Exception e) { { if (!_textWarned) { _textWarned = true; L.LogError($"[pdfl] settext: {e}"); } _shownText = null; } }
    }

    // ---------- 容器 / 文字对象 ----------

    private void EnsureAnchor()
    {
        if (_csc != null && (bool)_csc) return;
        _csc = null;
        _tm = null;
        _labelWarned = false;
        _colorsResolved = false;
        _curColor = new Color(-1f, 0f, 0f);
        _shownText = null;
        _nearSince = null;
        _brkSince = null;
        try { _csc = UnityEngine.Object.FindObjectOfType<ClearStateContainer>(); }
        catch (Exception e) { L.LogError($"[pdfl] find container: {e.Message}"); }
    }

    private static bool Usable(FText f) =>
        f != null && f.fontFamilyAsset != null && f.isActiveAndEnabled &&
        f.gameObject.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) < 0;

    private FText FindTemplate()
    {
        // 优先取局内 HUD 上（与容器同根）的 FastText，否则任意已有的
        try
        {
            var list = _csc.transform.root.GetComponentsInChildren<FText>(false);
            for (int i = 0; i < list.Length; i++) if (Usable(list[i])) return list[i];
        }
        catch { }
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<FText>();
            for (int i = 0; i < all.Length; i++) if (Usable(all[i])) return all[i];
        }
        catch { }
        return null;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        // ColorUtility 在此 IL2CPP 构建中不可用，手动解析 #RRGGBB / #RRGGBBAA
        try
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6 && h.Length != 8) return fallback;
            float B(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255f;
            return new Color(B(0), B(2), B(4), h.Length == 8 ? B(6) : 1f);
        }
        catch { return fallback; }
    }

    private void ResolveColors()
    {
        if (_colorsResolved || _csc == null) return;
        _colorsResolved = true;
        _pdColor = new Color(0.94f, 0.61f, 1f);
        _flColor = new Color(0.70f, 0.71f, 1f);
        _clrColor = new Color(0.61f, 0.91f, 0.97f);
        _clrColor = ParseColor(Plugin.CfgClearColor.Value, _clrColor);
        _pdColor = ParseColor(Plugin.CfgPdColor.Value, _pdColor);
        _flColor = ParseColor(Plugin.CfgFlColor.Value, _flColor);
        L.LogInfo($"[pdfl] colors pd={_pdColor} fl={_flColor}");
    }

    private void ApplyColor()
    {
        if (_tm == null || !(bool)_tm) return;
        ResolveColors();
        Color want = _brk > 0 ? _clrColor : (_near > 0 ? _flColor : _pdColor);
        if (want == _curColor) return;
        _curColor = want;
        try { _tm.SetTextColor(want); }
        catch (Exception e) { L.LogError($"[pdfl] color: {e.Message}"); }
    }

    private static string DescText(FText f)
    {
        if (f == null || !(bool)f) return "null";
        var sb = new StringBuilder();
        try
        {
            var go = f.gameObject; var t = go.transform;
            sb.Append($"'{go.name}' active={go.activeInHierarchy} layer={go.layer} pos={t.position} lossy={t.lossyScale} size={f.fontSize} text='{f.GetText()}'");
            var mr = f.meshRenderer;
            if (mr != null) sb.Append($" mr.en={mr.enabled} vis={mr.isVisible} bounds={mr.bounds} sortL={mr.sortingLayerID} sortO={mr.sortingOrder} mat='{(mr.sharedMaterial != null ? mr.sharedMaterial.name : "null")}' rq={(mr.sharedMaterial != null ? mr.sharedMaterial.renderQueue : -1)}");
            else sb.Append(" mr=null");
            var mf = f.meshFilter;
            if (mf != null && mf.sharedMesh != null) sb.Append($" verts={mf.sharedMesh.vertexCount}");
            else sb.Append(" mesh=null");
            var comps = go.GetComponents<Component>();
            sb.Append(" comps=");
            for (int i = 0; i < comps.Length; i++) sb.Append(comps[i].GetIl2CppType().Name).Append(',');
            if (t.parent != null) sb.Append($" parent='{t.parent.name}'");
        }
        catch (Exception e) { sb.Append(" ERR ").Append(e.Message); }
        return sb.ToString();
    }

    private void Diag()
    {
        L.LogInfo($"[pdfl][diag] stripped={_strippedNames}");
        L.LogInfo($"[pdfl][diag] tpl  {DescText(_tpl)}");
        L.LogInfo($"[pdfl][diag] mine {DescText(_tm)}");
        try { L.LogInfo($"[pdfl][diag] csc pos={_csc.transform.position} lossy={_csc.transform.lossyScale} parent='{_csc.transform.parent?.name}'"); } catch { }
    }

    private void FollowAnchor()
    {
        if (_tm == null || _csc == null) return;
        var a = _csc.transform;
        var t = _tm.transform;
        t.position = a.TransformPoint(new Vector3(0f, Plugin.CfgOffsetY.Value, -0.06f));
        t.rotation = a.rotation;
    }

    private void EnsureLabel()
    {
        if (_tm != null && (bool)_tm) return;
        _tm = null;
        if (_csc == null) return;

        try
        {
            var tpl = FindTemplate();
            if (tpl == null)
            {
                if (!_labelWarned) { _labelWarned = true; L.LogWarning("[pdfl] no FastText template yet"); }
                return;
            }

            // 克隆模板，让 FastText 走游戏自己的初始化；然后剥掉其它脚本和子物体
            _strippedNames = "";
            var go = UnityEngine.Object.Instantiate(tpl.gameObject, _csc.transform.parent, false);
            go.name = "pdfl-label";
            go.layer = _csc.gameObject.layer;
            var t = go.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
            var comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null || c.TryCast<Transform>() != null || c.TryCast<FText>() != null ||
                    c.TryCast<MeshFilter>() != null || c.TryCast<MeshRenderer>() != null) continue;
                _strippedNames += c.GetIl2CppType().Name + ",";
                UnityEngine.Object.DestroyImmediate(c);
            }

            // 保持与模板相同的世界尺寸
            var ls = tpl.transform.lossyScale;
            var ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            float k = Math.Max(0.01f, Plugin.CfgTextScale.Value);
            t.localScale = new Vector3(ls.x / ps.x * k, ls.y / ps.y * k, ls.z / ps.z);

            var ft = go.GetComponent<FText>();
            ft.SetAlignment(AlignmentOptions.Center);
            ft.SetTextColor(Color.white);

            _tm = ft;
            _tpl = tpl;
            _diagAt = Time.unscaledTime + 1.5f;
            FollowAnchor();

            L.LogInfo($"[pdfl] label created from template '{tpl.gameObject.name}' size={ft.fontSize} pos={t.position} scale={t.lossyScale}");
        }
        catch (Exception e)
        {
            if (!_labelWarned) { _labelWarned = true; L.LogError($"[pdfl] label: {e}"); }
        }
    }

    // ---------- 判定计数 ----------

    /// <summary>
    /// Result: 6=Perfect(Exact), 5/4=Perfect(Small), 3/2=Near, 1=Break。
    /// Hold 的中间态会被写成 Near 再翻回 Perfect，所以 NEAR/BREAK 要「持续 Stable 秒」才计入。
    /// </summary>
    private void CountJudgements(float now)
    {
        _exact = 0;
        _exactBig = 0;
        _exactSmall = 0;
        _near = 0;
        _brk = 0;
        try
        {
            var player = LogicalNotePlayer._Qe;
            if (player == null) return;
            var chart = player._ve;
            if (chart == null) return;
            var mem = chart._cd;
            if (mem == null) return;
            var owner = mem._object;
            if (owner == null) return;

            var arr = new Il2CppStructArray<_cH>(((Il2CppObjectBase)owner).Pointer);
            int off = mem._index;
            int len = chart._Dd;
            int cap = mem._length & 0x7FFFFFFF;
            if (off < 0 || len <= 0 || len > 120000 || len > cap) return;
            if (off + len > ((Il2CppArrayBase<_cH>)(object)arr).Length) return;

            if (_nearSince == null || _nearSince.Length < len)
            {
                _nearSince = new float[len];
                _brkSince = new float[len];
            }

            float need = Math.Max(0f, Plugin.CfgStable.Value);
            var span = NativeJudgementArray.View(arr);
            for (int k = 0; k < len; k++)
            {
                byte r = span[off + k].Result;
                bool isNear = r == 2 || r == 3;
                bool isBrk = r == 1;

                if (r == 6) _exactBig++;
                else if (r == 4 || r == 5) _exactSmall++;

                if (isNear)
                {
                    if (_nearSince[k] <= 0f) _nearSince[k] = now;
                    if (now - _nearSince[k] >= need) _near++;
                }
                else _nearSince[k] = 0f;

                if (isBrk)
                {
                    if (_brkSince[k] <= 0f) _brkSince[k] = now;
                    if (now - _brkSince[k] >= need) _brk++;
                }
                else _brkSince[k] = 0f;
            }
            _exact = _exactBig + _exactSmall;
        }
        catch (Exception e)
        {
            if (!_warned) { _warned = true; L.LogError($"[pdfl] count: {e.Message}"); }
        }
    }
}
