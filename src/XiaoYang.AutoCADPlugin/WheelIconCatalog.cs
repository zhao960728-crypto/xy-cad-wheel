using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XiaoYang.AutoCADPlugin;

internal sealed class WheelIconEntry
{
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Keywords { get; }
    internal WheelIconEntry(string id, string name, string category, string keywords)
    { Id = id; Name = name; Category = category; Keywords = keywords; }
}

// Stable IDs are independent of display language. Geometry uses a transparent
// 32-unit canvas, with semantic operation badges rather than color-only variants.
internal static class WheelIconCatalog
{
    private static readonly string[] Names = (
        "直线|多段线|圆|圆弧|矩形|椭圆|多边形|样条曲线|构造线|射线|点|圆环|填充|边界|螺旋|修订云线|" +
        "移动|复制|旋转|缩放|镜像|偏移|修剪|延伸|圆角|倒角|拉伸|分解|合并|打断|删除|阵列|" +
        "线性标注|对齐标注|角度标注|半径标注|直径标注|坐标标注|弧长标注|连续标注|基线标注|引线|多行文字|单行文字|表格|公差|中心线|测量|" +
        "新建图纸|打开文件|保存图纸|另存为|打印|导出|导入|撤销|重做|剪切|粘贴|图层|块|外部参照|属性|清理|" +
        "放大|缩小|平移|全图|上个视图|三维环绕|俯视|前视|侧视|等轴测|线框|着色|相机|视口|坐标系|显示|" +
        "墙体|门|窗|楼梯|屋顶|房屋|柱|梁|基础|道路|桥梁|管道|阀门|水龙头|插座|灯具|" +
        "扳手|锤子|螺丝刀|钻头|齿轮|螺栓|螺母|尺子|圆规|铅笔|画笔|橡皮|磁铁|水平仪|工具箱|安全帽|" +
        "文件夹|云端|下载|上传|链接|地球|定位|指南针|时钟|日历|用户|团队|邮件|电话|音乐|爱心|钣金展开|焊接|切割|折弯|排版套料|重量|面积|体积|统计图|数据库|脚本|插件|检查|编号|二维码|条形码|图片|文档|同步|备份|锁|解锁|筛选|排序|对比|连接节点|路径规划|电路|轴承|弹簧|机器人|五角星|熊猫").Split('|');
    private static readonly string[] Keys = (
        "line|polyline|circle|arc|rectangle|ellipse|polygon|spline|xline|ray|point|donut|hatch|boundary|helix|revcloud|" +
        "move|copy|rotate|scale|mirror|offset|trim|extend|fillet|chamfer|stretch|explode|join|break|erase|array|" +
        "dimlinear|dimaligned|dimangular|dimradius|dimdiameter|dimordinate|dimarc|dimcontinue|dimbaseline|leader|mtext|text|table|tolerance|centerline|measure|" +
        "new|open|save|saveas|plot|export|import|undo|redo|cut|paste|layer|block|xref|properties|purge|" +
        "zoom-in|zoom-out|pan|extents|previous-view|orbit|top|front|side|isometric|wireframe|shade|camera|viewport|ucs|display|" +
        "wall|door|window|stairs|roof|house|column|beam|foundation|road|bridge|pipe|valve|tap|socket|lamp|" +
        "wrench|hammer|screwdriver|drill|gear|bolt|nut|ruler|compass-tool|pencil|brush|eraser|magnet|level|toolbox|helmet|" +
        "folder|cloud|download|upload|link|globe|pin|compass|clock|calendar|user|team|mail|phone|music|heart|sheet-unfold|weld|laser-cut|bend|nesting|weight|area|volume|chart|database|script|plugin|validate|numbering|qrcode|barcode|image|document|sync|backup|lock|unlock|filter|sort|compare|nodes|route|circuit|bearing|spring|robot|star|panda").Split('|');
    private static readonly string[] Categories = { "CAD 绘制", "CAD 修改", "CAD 标注", "CAD 文件与管理", "CAD 视图", "建筑与设施", "工具与机械", "通用与生活", "钣金工具" };
    private static readonly string[] Actions = { "", "新增", "编辑", "复制", "删除", "查找", "锁定", "收藏" };
    private static readonly string[] ActionKeys = { "", "add", "edit", "copy", "delete", "search", "lock", "star" };
    private static readonly Dictionary<string, int> Index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> Commands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<WheelIconEntry> Entries { get; }

    static WheelIconCatalog()
    {
        string[] aliases = {
            "LINE L", "PLINE PL 3DPOLY", "CIRCLE C", "ARC A", "RECTANG RECTANGLE REC", "ELLIPSE EL", "POLYGON POL", "SPLINE SPL", "XLINE XL", "RAY", "POINT PO DIVIDE", "DONUT DO", "HATCH H BHATCH HATCHEDIT HE", "BOUNDARY BO BPOLY", "HELIX", "REVCLOUD",
            "MOVE M", "COPY CO CP COPYBASE", "ROTATE RO", "SCALE SC", "MIRROR MI MIRROR3D", "OFFSET O", "TRIM TR", "EXTEND EX", "FILLET F", "CHAMFER CHA", "STRETCH S", "EXPLODE X", "JOIN J PEDIT PE", "BREAK BR BREAKATPOINT", "ERASE E", "ARRAY AR ARRAYRECT ARRAYPOLAR ARRAYPATH",
            "DIMLINEAR DLI DIM", "DIMALIGNED DAL", "DIMANGULAR DAN", "DIMRADIUS DRA", "DIMDIAMETER DDI", "DIMORDINATE DOR", "DIMARC DAR", "DIMCONTINUE DCO", "DIMBASELINE DBA", "LEADER LE QLEADER MLEADER MLD", "MTEXT MT T", "TEXT DTEXT DT", "TABLE TB", "TOLERANCE TOL", "CENTERLINE CENTERMARK DIMCENTER", "MEASURE ME DIST DI AREA AA MEASUREGEOM ID",
            "NEW QNEW", "OPEN", "SAVE QSAVE", "SAVEAS", "PLOT PRINT PREVIEW", "EXPORT WBLOCK PDFEXPORT DXFOUT", "IMPORT INSERT I PDFIMPORT DXFIN", "UNDO U", "REDO MREDO", "CUTCLIP", "PASTECLIP PASTEBLOCK PASTEORIG", "LAYER LA LAYISO LAYUNISO LAYOFF LAYON LAYFRZ LAYTHW LAYLCK LAYULK", "BLOCK B BEDIT BCLOSE REFEDIT", "XREF XR XATTACH XOPEN", "PROPERTIES PR CH MATCHPROP MA ATTEDIT EATTEDIT", "PURGE PU AUDIT RECOVER OVERKILL",
            "ZOOM Z", "", "PAN P", "ZOOMEXTENTS", "VIEW V", "3DORBIT ORBIT", "PLAN", "", "", "ISOPLANE", "VSCURRENT", "SHADE SHADEMODE VISUALSTYLES", "CAMERA", "MVIEW MV VPORTS", "UCS UCSICON", "REGEN RE REGENALL REDRAW R"
        };
        for (int i = 0; i < aliases.Length; i++)
            foreach (string alias in aliases[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) Commands.Add(alias, i);
        Commands.Add("EXTRUDE", 73);
        Commands.Add("EXT", 73);
        Commands.Add("PRESSPULL", 73);
        Commands.Add("REVOLVE", 69);
        Commands.Add("SWEEP", 91);
        Commands.Add("LOFT", 75);
        Commands.Add("BOX", 74);
        AddPluginCommands();
        var commandKeywords = new string[Names.Length];
        foreach (var mapping in Commands) commandKeywords[mapping.Value] += " " + mapping.Key;
        var entries = new List<WheelIconEntry>(1024);
        for (int operation = 0; operation < Actions.Length; operation++)
        for (int i = 0; i < Names.Length; i++)
        {
            var id = Keys[i] + (operation == 0 ? "" : "." + ActionKeys[operation]);
            Index.Add(id, i * 8 + operation);
            entries.Add(new WheelIconEntry(id, Names[i] + (operation == 0 ? "" : " · " + Actions[operation]), (i > 128 ? "专业与扩展工具" : Categories[i / 16]), Keys[i] + " " + Names[i] + " " + Actions[operation] + " " + ActionKeys[operation] + commandKeywords[i] + (SemanticKeywords.TryGetValue(Keys[i], out var synonyms) ? " " + synonyms.Replace('|', ' ') : "")));
        }
        Entries = entries.AsReadOnly();
    }

    private static void AddPluginCommands()
    {
        // Exact public command names from the installed command library. Do not
        // infer matches from SUN/SS/XYCUT prefixes: user commands may share them.
        string[] groups = {
            "toolbox:SS000 SUN SUN-085 SUN-102 SUN-162 SUN-181 SUN-240 SUN-KJJ",
            "gear:SS001 SUN-153",
            "rectangle:SS002 SS003 SS004 SUN-026 SUN-032 SUN-033 SUN-087 SUN-088 SUN-089 SUN-139 SUN-178 SUN-210 SUN-219 SUN-225",
            "export:SS005 SS006 SS012 SUN-186 SUN-239",
            "import:SS007",
            "folder:SS008 SUN-143 SUN-213 SUN-224",
            "array:SS009 SS201 SUN-116 SUN-173",
            "move:SS010",
            "copy:SS011",
            "arc:SS013",
            "join:SS014 SUN-152 SUN-206 SUN-209 SUN-226 SUN-227 SUN-235",
            "drill:SS015 SUN-023 SUN-024 SUN-029 SUN-098 SUN-170 SUN-211 SUN-221",
            "dimlinear:SUN-001 SUN-002 SUN-003 SUN-004 SUN-007 SUN-012 SUN-013 SUN-014 SUN-015 SUN-016 SUN-055 SUN-086 SUN-092 SUN-123 SUN-127 SUN-144 SUN-145 SUN-158 SUN-172 SUN-174 SUN-177 SUN-187 SUN-197 SUN-203 SUN-212 SUN-216 SUN-236 SUN-237 SUN-238 XYCUT-SIMPLE-ANNOTATION",
            "dimdiameter:SUN-005 SUN-010 SUN-134 SUN-135 SUN-157",
            "tolerance:SUN-006",
            "dimarc:SUN-008",
            "bolt:SUN-009 SUN-011 SUN-099",
            "dimordinate:SUN-017 SUN-159 SUN-196",
            "sheet-unfold:SUN-018 SUN-019 SUN-020 SUN-103 SUN-124 SUN-131 SUN-132 SUN-133 SUN-171 SUN-188 SUN-200 SUN-215 SUN-218 XYCUT-UNFOLD XYUNFOLD",
            "break:SUN-021 SUN-126 SUN-207",
            "table:SUN-022 SUN-037 SUN-105 SUN-180 SUN-184 SUN-195 SUN-199 SUN-204 SUN-241 SUN-242 XYCUT-FLAT-QUANTITY-SUMMARY",
            "centerline:SUN-025",
            "xline:SUN-027 SUN-028 SUN-128",
            "zoom-in:SUN-030 SUN-106",
            "scale:SUN-031 SUN-100 SUN-234",
            "erase:SUN-034 SUN-038 SUN-050 SUN-107 SUN-110 SUN-121 SUN-147 SUN-163 SUN-182 SUN-191 SUN-214",
            "magnet:SUN-035 SUN-039 SUN-040 SUN-044 SUN-082 SUN-091 SUN-129 SUN-154",
            "level:SUN-036 SUN-078",
            "chamfer:SUN-041 SUN-175 SUN-176",
            "fillet:SUN-042 SUN-093",
            "stretch:SUN-043",
            "mirror:SUN-045 SUN-047",
            "rotate:SUN-046 SUN-122",
            "circle:SUN-048 SUN-049 SUN-051 SUN-166 SUN-179 SUN-205",
            "polyline:SUN-052 SUN-053 SUN-095 SUN-231",
            "ucs:SUN-054 SUN-083 SUN-138",
            "purge:SUN-056 SUN-148 XYCUT-FLAT-EXTRACT",
            "layer:SUN-057 SUN-058 SUN-059 SUN-060 SUN-061 SUN-062 SUN-063 SUN-064 SUN-065 SUN-066 SUN-067 SUN-068 SUN-069 SUN-070 SUN-071 SUN-073 SUN-090 SUN-137 SUN-161",
            "globe:SUN-072",
            "text:SUN-074 SUN-075 SUN-076 SUN-077 SUN-079 SUN-094 SUN-101 SUN-108 SUN-109 SUN-114 SUN-190 SUN-193 SUN-208 SUN-217 SUN-220 SUN-222 SUN-223",
            "explode:SUN-080 SUN-233",
            "window:SUN-081",
            "plot:SUN-084 SUN-151",
            "offset:SUN-096 SUN-104 SUN-164",
            "block:SUN-097 SUN-111 SUN-113 SUN-198 SUN-201 SUN-202",
            "copy:SUN-112",
            "point:SUN-115",
            "display:SUN-117 SUN-118 SUN-119 SUN-230",
            "trim:SUN-120",
            "measure:SUN-125 SUN-130",
            "polygon:SUN-136",
            "revcloud:SUN-140",
            "leader:SUN-141 SUN-146 SUN-149",
            "calendar:SUN-142",
            "dimangular:SUN-150 SUN-156 SUN-192",
            "dimaligned:SUN-155",
            "dimradius:SUN-160",
            "brush:SUN-165",
            "line:SUN-167",
            "side:SUN-168",
            "pencil:SUN-169",
            "properties:SUN-183 SUN-194 SUN-232",
            "save:SUN-185",
            "hatch:SUN-189",
            "link:SUN-228",
            "bridge:SUN-229",
            // Public entry points and ready commands from CAD自动展开插件.
            "sheet-unfold:XYBZ",
            "toolbox:MCXY XYCUTBOX XYCUTBOXRELOAD XYCUTBOXSELFTEST XYUNFOLDVERIFY",
            "scale:XYCUT-SCALE XYCUT-DIM-SCALE XYCUT-GEOMETRY-ONE-TO-ONE",
            "saveas:XYCUT-SAVEAS",
            "export:XYCUT-CAD-TO-EXCEL",
            "import:XYCUT-EXCEL-TO-CAD",
            "measure:XYCUT-AREA-WEIGHT",
            "join:XYCUT-MERGEDWG",
            "plot:XYCUT-BATCH-PRINT",
            "zoom-in:XYCUT-DETAIL-VIEW",
            "boundary:XYCUT-NESTING-RECOGNITION",
            "revcloud:XYCUT-COMPARE",
            "erase:XYCUT-DELETE-THREAD-CROSS XYCUT-DELETE-CONCENTRIC XYCUT-OBSOLETE",
            "drill:XYCUT-OB-HOLE XYCUT-ALL-HOLES XYCUT-PROCESS-HOLE",
            "bolt:XYCUT-TAP-NOTE",
            "leader:XYCUT-BEND-NOTE",
            "level:XYCUT-HORIZONTAL-ALIGN",
            "array:XYCUT-FRAME-ARRANGE",
            "dimlinear:XYCUT-OUTLINE-DIM XYCUT-BATCH-OUTLINE-DIM",
            "table:XYCUT-GJ-NUMBER-SUM XYCUT-HOLE-STATS-MARK",
            "circle:XYCUT-SAME-CIRCLE",
            "dimcontinue:XYCUT-DIM-CHAIN",
            "properties:XYCUT-PROPERTIES",
            "layer:XYCUT-LAYER-NORMALIZE XYCUT-LAYERS-ON XYCUT-LAYER-OFF XYCUT-LAYER-ISOLATE",
            "brush:XYCUT-SELECT-SAME-COLOR",
            "text:XYCUT-SELECTED-SIMSUN XYCUT-SIMSUN"
        };
        foreach (string group in groups)
        {
            int separator = group.IndexOf(':');
            int icon = Array.IndexOf(Keys, group.Substring(0, separator));
            if (icon < 0) throw new InvalidOperationException("未知命令图标映射。" );
            foreach (string command in group.Substring(separator + 1).Split(' '))
                Commands.Add(command, icon);
        }
    }

    public static Bitmap Render(string id, int size)
    {
        int index;
        if (id == null || !Index.TryGetValue(id, out index)) throw new ArgumentException("未知图标。", nameof(id));
        if (size < 16 || size > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try { using (var g = Graphics.FromImage(bitmap)) Draw(g, index, new PointF(size / 2f, size / 2f), size); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }

    public static bool TryGetCommandIconId(string command, out string id)
    {
        int index;
        var matched = TryCommand(command, out index);
        id = matched ? Keys[index] : "";
        return matched;
    }

    // Descriptive metadata is a fallback only: aliases never redefine the executed command.
    // Matching is deterministic and read-only, so a user's explicit image is never replaced.
    public static bool TryGetSemanticIconId(string command, string name, string description, string category, out string id)
    {
        if (TryGetCommandIconId(command, out id)) return true;
        int best = 0;
        for (int i = 0; i < Keys.Length; i++)
        {
            int score = SemanticScore(name, Names[i], Keys[i]) * 4
                + SemanticScore(description, Names[i], Keys[i]) * 2
                + SemanticScore(category, Names[i], Keys[i]);
            if (score > best) { best = score; id = Keys[i]; }
        }
        return best > 0;
    }

    public static bool TryDrawSemantic(Graphics g, string command, string name, string description, string category, PointF center, float size)
    {
        if (g == null || !TryGetSemanticIconId(command, name, description, category, out var id)) return false;
        Draw(g, Index[id], center, size);
        return true;
    }

    private static readonly Dictionary<string, string> SemanticKeywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dimlinear"] = "标注|尺寸|dimension|annotation", ["measure"] = "测距|测量|measure|distance",
        ["text"] = "文字|字体|文本|text|font", ["table"] = "表格|报表|清单|excel|spreadsheet|report",
        ["layer"] = "图层|layer", ["block"] = "图块|块定义|block", ["drill"] = "孔|钻孔|hole|drill",
        ["sheet-unfold"] = "展开|放样|unfold|flatten", ["laser-cut"] = "切割|激光|下料|cutting|laser",
        ["weld"] = "焊接|焊缝|weld|welding", ["bend"] = "折弯|弯曲|bend|bending",
        ["nesting"] = "套料|排样|排版|nesting", ["weight"] = "重量|质量|称重|weight|mass",
        ["area"] = "面积|area", ["volume"] = "体积|容积|volume", ["chart"] = "统计|图表|分析|chart|statistics|analysis",
        ["database"] = "数据库|数据管理|database|sql", ["script"] = "脚本|编程|自动化|script|lisp|automation",
        ["plugin"] = "插件|扩展|plugin|extension|addin", ["validate"] = "检查|校验|验证|validate|check|inspection",
        ["numbering"] = "编号|序号|编码|numbering", ["qrcode"] = "二维码|qr", ["barcode"] = "条形码|条码|barcode",
        ["image"] = "图片|图像|截图|image|photo|screenshot", ["document"] = "文档|说明书|document|pdf",
        ["sync"] = "同步|刷新|sync|refresh", ["backup"] = "备份|恢复备份|backup", ["lock"] = "锁定|加锁|lock",
        ["unlock"] = "解锁|unlock", ["filter"] = "筛选|过滤|filter", ["sort"] = "排序|sort",
        ["compare"] = "对比|比较|差异|compare|difference", ["nodes"] = "连接节点|网络|拓扑|network|topology",
        ["route"] = "路径规划|布线|寻路|routing", ["circuit"] = "电路|电气|电线|circuit|electrical",
        ["bearing"] = "轴承|bearing", ["spring"] = "弹簧|spring", ["robot"] = "机器人|robot",
        ["plot"] = "打印|出图|print|plot", ["export"] = "导出|export", ["import"] = "导入|import",
        ["erase"] = "删除|清除|delete|erase", ["purge"] = "清理|去重|purge|deduplicate"
    };

    private static int SemanticScore(string value, string name, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        // A single Chinese character (e.g. 点/圆) is too broad except for an exact name.
        int score = string.Equals(value.Trim(), name, StringComparison.OrdinalIgnoreCase) ? 16 : 0;
        if (name.Length > 1 && ContainsKeyword(value, name)) score = Math.Max(score, name.Length + 2);
        if (ContainsKeyword(value, key)) score = Math.Max(score, key.Length + 2);
        if (SemanticKeywords.TryGetValue(key, out var keywords))
            foreach (var keyword in keywords.Split('|'))
                if (ContainsKeyword(value, keyword)) score = Math.Max(score, keyword.Length + 2);
        return score;
    }

    private static bool ContainsKeyword(string value, string keyword)
    {
        int start = 0;
        while (start < value.Length)
        {
            int p = value.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return false;
            bool latin = keyword[0] < 128;
            if (!latin || ((p == 0 || !IsLatinWord(value[p - 1]))
                && (p + keyword.Length == value.Length || !IsLatinWord(value[p + keyword.Length])))) return true;
            start = p + keyword.Length;
        }
        return false;
    }
    private static bool IsLatinWord(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9';

    public static bool TryDrawCommand(Graphics g, string command, PointF center, float size)
    {
        int index;
        if (g == null || !TryCommand(command, out index)) return false;
        Draw(g, index * 8, center, size);
        return true;
    }

    // Only whole command tokens match: a user's MYLINE/LISP expression must not
    // accidentally receive LINE artwork. Consume common AutoCAD macro prefixes.
    private static bool TryCommand(string command, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(command)) return false;
        int p = 0;
        while (p < command.Length)
        {
            char c = command[p];
            if (char.IsWhiteSpace(c) || c == '\x03' || c == '\x1b' || c == ';' || c == '_' || c == '.' || c == '-' || c == '\'') { p++; continue; }
            if (c == '^' && p + 1 < command.Length && (command[p + 1] == 'C' || command[p + 1] == 'c')) { p += 2; continue; }
            break;
        }
        int start = p;
        while (p < command.Length && !char.IsWhiteSpace(command[p]) && command[p] != ';') p++;
        if (!Commands.TryGetValue(command.Substring(start, p - start), out index)) return false;
        // ZOOM variants share the exact command token but have different symbols.
        if (index == 64)
        {
            string rest = command.Substring(p).Trim(' ', '\t', '\r', '\n', ';', '_').ToUpperInvariant();
            if (rest == "E" || rest == "EXTENTS") index = 67;
            else if (rest == "P" || rest == "PREVIOUS") index = 68;
            else if (rest == "0.5X" || rest == "OUT") index = 65;
        }
        return true;
    }

    private static void Draw(Graphics g, int index, PointF center, float size)
    {
        if (size <= 0 || float.IsNaN(size) || float.IsInfinity(size)) return;
        var state = g.Save();
        try
        {
            g.TranslateTransform(center.X - size / 2, center.Y - size / 2);
            g.ScaleTransform(size / 32, size / 32);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var blue = new Pen(Color.FromArgb(58, 158, 238), 1.9f))
            using (var cyan = new Pen(Color.FromArgb(64, 211, 205), 1.9f))
            using (var orange = new Pen(Color.FromArgb(255, 181, 73), 1.9f))
            {
                blue.StartCap = blue.EndCap = cyan.StartCap = cyan.EndCap = orange.StartCap = orange.EndCap = LineCap.Round;
                blue.LineJoin = cyan.LineJoin = orange.LineJoin = LineJoin.Round;
                int operation = index % 8;
                if (operation != 0) { g.TranslateTransform(0, 0); g.ScaleTransform(.82f, .82f); }
                Shape(g, blue, cyan, orange, index / 8);
                if (operation != 0) { g.ScaleTransform(1 / .82f, 1 / .82f); Badge(g, orange, operation); }
            }
        }
        finally { g.Restore(state); }
    }

    private static PointF[] Points(params float[] xy)
    { var p = new PointF[xy.Length / 2]; for (int i = 0; i < p.Length; i++) p[i] = new PointF(xy[2 * i], xy[2 * i + 1]); return p; }
    private static void Lines(Graphics g, Pen p, params float[] xy) { g.DrawLines(p, Points(xy)); }
    private static void Poly(Graphics g, Pen p, params float[] xy) { g.DrawPolygon(p, Points(xy)); }
    private static void Arrow(Graphics g, Pen p, float x1, float y1, float x2, float y2)
    {
        g.DrawLine(p, x1, y1, x2, y2);
        double a = Math.Atan2(y2 - y1, x2 - x1);
        g.DrawLine(p, x2, y2, x2 - 4 * (float)Math.Cos(a - .55), y2 - 4 * (float)Math.Sin(a - .55));
        g.DrawLine(p, x2, y2, x2 - 4 * (float)Math.Cos(a + .55), y2 - 4 * (float)Math.Sin(a + .55));
    }
    private static void Box(Graphics g, Pen p, float x = 7, float y = 7, float w = 18, float h = 18) { g.DrawRectangle(p, x, y, w, h); }
    private static void Cube(Graphics g, Pen p)
    { Poly(g, p, 16,4,27,10,27,23,16,29,5,23,5,10); Lines(g,p,5,10,16,16,27,10); Lines(g,p,16,16,16,29); }
    private static void Page(Graphics g, Pen p)
    { Poly(g,p,7,4,20,4,26,10,26,28,7,28); Lines(g,p,20,4,20,10,26,10); }
    private static void Star(Graphics g, Pen p, float cx, float cy, float r)
    {
        var points = new PointF[10];
        for (int i=0;i<10;i++) { double a = -Math.PI/2+i*Math.PI/5; float d=i%2==0?r:r*.44f; points[i]=new PointF(cx+d*(float)Math.Cos(a),cy+d*(float)Math.Sin(a)); }
        g.DrawPolygon(p,points);
    }
    private static void Badge(Graphics g, Pen p, int operation)
    {
        using(var fill = new SolidBrush(Color.FromArgb(245, 28, 42, 58))) g.FillEllipse(fill,19,19,13,13);
        float x=25.3f,y=25.3f;
        switch(operation)
        {
            case 1: g.DrawLine(p,x-3,y,x+3,y);g.DrawLine(p,x,y-3,x,y+3); break;
            case 2: Lines(g,p,22,28,23,25,27,21,29,23,25,27,22,28); break;
            case 3: Box(g,p,22,22,5,5);Lines(g,p,25,24,29,24,29,29,24,29);break;
            case 4: Lines(g,p,22,22,28,28);Lines(g,p,28,22,22,28);break;
            case 5: g.DrawEllipse(p,21,21,6,6);g.DrawLine(p,26,26,29,29);break;
            case 6: Box(g,p,22,25,7,5);g.DrawArc(p,23,20,5,8,180,180);break;
            case 7: Star(g,p,x,y,4.5f);break;
        }
    }

    private static void Shape(Graphics g, Pen b, Pen c, Pen a, int s)
    {
        switch(s)
        {
            case 0: Lines(g,b,6,25,26,7); Box(g,c,4,23,4,4);Box(g,c,24,5,4,4);break;
            case 1: Lines(g,b,5,25,10,8,21,22,27,6); foreach(var p in Points(5,25,10,8,21,22,27,6))g.DrawEllipse(c,p.X-1.5f,p.Y-1.5f,3,3);break;
            case 2: g.DrawEllipse(b,5,5,22,22);g.DrawLine(c,13,16,19,16);g.DrawLine(c,16,13,16,19);break;
            case 3: g.DrawArc(b,5,6,23,23,185,165);g.DrawEllipse(c,3,14,4,4);g.DrawEllipse(c,25,14,4,4);break;
            case 4: Box(g,b,4,8,24,17);Box(g,c,4,21,4,4);break;
            case 5: g.DrawEllipse(b,3,9,26,14);g.DrawLine(c,6,16,26,16);break;
            case 6: Poly(g,b,16,4,27,12,23,26,9,26,5,12);break;
            case 7: g.DrawBezier(b,4,23,10,-5,22,39,28,8);Box(g,c,2,21,3,3);Box(g,c,27,6,3,3);break;
            case 8: g.DrawLine(b,3,29,29,3);g.DrawLine(c,3,3,29,29);g.DrawEllipse(a,13,13,6,6);break;
            case 9: Arrow(g,b,6,25,28,5);g.DrawEllipse(c,4,23,4,4);break;
            case 10: g.DrawEllipse(a,13,13,6,6);g.DrawLine(b,16,4,16,10);g.DrawLine(b,16,22,16,28);g.DrawLine(b,4,16,10,16);g.DrawLine(b,22,16,28,16);break;
            case 11: g.DrawEllipse(b,4,4,24,24);g.DrawEllipse(c,10,10,12,12);break;
            case 12: Box(g,b,5,5,22,22);for(int x=7;x<27;x+=5)g.DrawLine(c,x,25,25,x);break;
            case 13: Poly(g,b,5,5,24,5,24,13,28,13,28,26,5,26);Poly(g,c,10,10,19,10,19,18,23,18,23,21,10,21);break;
            case 14: for(int i=0;i<4;i++)g.DrawArc(i%2==0?b:c,5,4+i*5,22,10,0,300);break;
            case 15: g.DrawArc(b,3,10,11,12,90,230);g.DrawArc(b,10,4,13,13,170,230);g.DrawArc(b,20,10,10,13,230,220);g.DrawArc(b,9,18,16,10,0,180);break;
            case 16: Arrow(g,b,16,16,16,4);Arrow(g,b,16,16,28,16);Arrow(g,b,16,16,16,28);Arrow(g,b,16,16,4,16);break;
            case 17: Box(g,b,5,5,15,15);Box(g,c,12,12,15,15);break;
            case 18: g.DrawArc(b,5,5,22,22,30,290);Lines(g,c,22,3,27,8,21,10);Box(g,a,12,12,8,8);break;
            case 19: Box(g,b,4,17,11,11);Box(g,c,13,4,15,15);Arrow(g,a,9,23,24,8);break;
            case 20: Poly(g,b,5,8,12,8,12,24,5,24,8,16);Poly(g,c,27,8,20,8,20,24,27,24,24,16);g.DrawLine(a,16,3,16,29);break;
            case 21: Lines(g,b,4,25,4,7,21,7);Lines(g,c,10,27,10,13,27,13);Arrow(g,a,18,21,25,21);break;
            case 22: Lines(g,b,5,7,27,7);Lines(g,b,9,4,9,28);Lines(g,c,9,23,27,23);g.DrawLine(a,19,17,26,28);g.DrawLine(a,26,17,19,28);break;
            case 23: g.DrawLine(b,26,4,26,28);g.DrawLine(c,4,18,15,18);Arrow(g,a,16,18,25,18);break;
            case 24: Lines(g,b,5,27,5,15);g.DrawArc(b,5,5,20,20,180,90);Lines(g,b,15,5,27,5);g.DrawArc(c,12,12,10,10,180,90);break;
            case 25: Lines(g,b,5,27,5,15,15,5,27,5);g.DrawLine(c,10,22,22,10);break;
            case 26: Poly(g,b,4,9,18,9,26,24,4,24);Arrow(g,c,15,16,28,16);break;
            case 27: Lines(g,b,5,12,5,5,12,5);Lines(g,b,20,5,27,5,27,12);Lines(g,c,27,20,27,27,20,27);Lines(g,c,12,27,5,27,5,20);g.DrawLine(a,12,12,20,20);g.DrawLine(a,20,12,12,20);break;
            case 28: Lines(g,b,4,9,12,9,16,16);Lines(g,c,16,16,21,23,28,23);g.DrawEllipse(a,12,12,8,8);break;
            case 29: Lines(g,b,4,24,13,15);Lines(g,c,19,9,28,0+4);Lines(g,a,12,9,19,16);break;
            case 30: Lines(g,b,8,9,10,28,23,28,25,9);Lines(g,c,5,8,28,8);Lines(g,c,12,8,12,4,21,4,21,8);g.DrawLine(b,14,13,14,23);g.DrawLine(b,20,13,20,23);break;
            case 31: for(int y=0;y<3;y++)for(int x=0;x<3;x++)Box(g,(x+y)%2==0?b:c,4+x*9,4+y*9,6,6);break;
            case 32: g.DrawLine(b,6,8,6,28);g.DrawLine(b,26,8,26,28);Arrow(g,c,16,13,6,13);Arrow(g,c,16,13,26,13);g.DrawLine(a,11,24,21,24);break;
            case 33: g.DrawLine(b,3,18,13,28);g.DrawLine(b,19,2,29,12);Arrow(g,c,16,16,8,24);Arrow(g,c,16,16,24,8);break;
            case 34: Lines(g,b,27,26,5,26,21,5);g.DrawArc(c,0,9,32,32,305,55);break;
            case 35: g.DrawEllipse(b,5,5,22,22);Arrow(g,c,16,16,25,9);g.DrawEllipse(a,15,15,2,2);break;
            case 36: g.DrawEllipse(b,5,5,22,22);Arrow(g,c,16,16,7,25);Arrow(g,c,16,16,25,7);break;
            case 37: Lines(g,b,5,4,5,27,28,27);Lines(g,c,12,16,20,16,20,6,28,6);g.DrawEllipse(a,10,14,4,4);break;
            case 38: g.DrawArc(b,4,9,24,24,190,150);g.DrawArc(c,4,3,24,24,190,150);g.DrawLine(a,3,12,8,15);g.DrawLine(a,24,15,29,12);break;
            case 39: for(int x=5;x<=27;x+=11)g.DrawLine(b,x,7,x,27);Arrow(g,c,15,14,5,14);Arrow(g,c,16,14,27,14);g.DrawLine(a,5,24,27,24);break;
            case 40: Lines(g,b,5,5,5,27,27,27);Arrow(g,c,5,9,17,9);Arrow(g,c,5,17,27,17);g.DrawLine(b,17,6,17,22);break;
            case 41: Lines(g,b,28,7,17,7,6,25);Lines(g,c,5,18,6,25,13,22);g.DrawLine(a,20,12,28,12);break;
            case 42: Lines(g,b,4,25,4,6,11,17,18,6,18,25);g.DrawLine(c,22,8,28,8);g.DrawLine(c,22,15,28,15);g.DrawLine(c,22,22,28,22);break;
            case 43: Lines(g,b,5,7,27,7);g.DrawLine(b,16,7,16,27);g.DrawLine(c,11,27,21,27);break;
            case 44: Box(g,b,4,5,24,22);g.DrawLine(c,4,12,28,12);g.DrawLine(b,4,20,28,20);g.DrawLine(b,12,12,12,27);g.DrawLine(b,21,12,21,27);break;
            case 45: Box(g,b,3,10,26,13);g.DrawLine(c,11,10,11,23);g.DrawLine(c,22,10,22,23);g.DrawEllipse(a,5,14,4,4);Lines(g,a,14,19,17,13,20,19);g.DrawLine(a,25,14,25,19);break;
            case 46: g.DrawEllipse(b,7,7,18,18);g.DrawLine(c,16,3,16,29);g.DrawLine(c,3,16,29,16);break;
            case 47: Lines(g,b,5,7,5,26,27,26);for(int x=9;x<28;x+=4)g.DrawLine(c,x,22,x,26);g.DrawLine(a,9,8,25,8);g.DrawLine(a,9,5,9,11);g.DrawLine(a,25,5,25,11);break;
            case 48: Page(g,b);g.DrawLine(c,11,18,22,18);g.DrawLine(c,16,13,16,23);break;
            case 49: Lines(g,b,4,26,4,8,12,8,15,12,28,12);Poly(g,c,4,26,9,16,29,16,24,26);break;
            case 50: Poly(g,b,5,5,24,5,28,9,28,28,5,28);Box(g,c,10,5,12,8);Box(g,c,10,20,13,8);break;
            case 51: Page(g,b);Lines(g,c,13,25,15,19,25,9,29,13,19,23,13,25);break;
            case 52: Box(g,b,4,11,24,13);Box(g,c,9,4,14,7);Box(g,c,9,20,14,9);g.DrawLine(a,23,15,25,15);break;
            case 53: Page(g,b);Arrow(g,c,13,19,29,19);break;
            case 54: Page(g,b);Arrow(g,c,29,19,13,19);break;
            case 55: g.DrawArc(b,7,10,20,17,200,250);Lines(g,c,5,7,5,17,14,17);break;
            case 56: g.DrawArc(b,5,10,20,17,90,250);Lines(g,c,27,7,27,17,18,17);break;
            case 57: g.DrawEllipse(b,4,21,8,8);g.DrawEllipse(b,20,21,8,8);g.DrawLine(c,8,23,26,4);g.DrawLine(c,24,23,6,4);break;
            case 58: Box(g,b,7,6,19,23);Box(g,c,11,3,11,6);g.DrawLine(c,12,16,22,16);g.DrawLine(c,12,22,22,22);break;
            case 59: Poly(g,b,3,11,16,4,29,11,16,18);Lines(g,c,3,17,16,24,29,17);Lines(g,a,3,23,16,30,29,23);break;
            case 60: Cube(g,b);g.DrawEllipse(c,12,10,8,8);break;
            case 61: Page(g,b);Box(g,c,3,17,11,11);g.DrawLine(a,12,20,21,12);break;
            case 62: for(int y=7;y<29;y+=8)g.DrawLine(b,4,y,28,y);Box(g,c,10,4,5,6);Box(g,c,21,12,5,6);Box(g,c,7,20,5,6);break;
            case 63: Lines(g,b,22,4,13,16);Poly(g,c,12,14,22,22,16,29,4,21);g.DrawLine(a,8,22,14,27);g.DrawLine(a,12,19,18,24);break;
            case 64: g.DrawEllipse(b,4,4,17,17);g.DrawLine(b,19,19,28,28);g.DrawLine(c,8,12,17,12);g.DrawLine(c,12,8,12,17);break;
            case 65: g.DrawEllipse(b,4,4,17,17);g.DrawLine(b,19,19,28,28);g.DrawLine(c,8,12,17,12);break;
            case 66: Lines(g,b,7,18,7,12,10,10,13,14,13,5,16,4,18,6,18,13,22,9,25,11,24,15,28,14,29,18,24,27,13,27,6,20,7,18);break;
            case 67: Arrow(g,b,12,12,4,4);Arrow(g,b,20,12,28,4);Arrow(g,b,12,20,4,28);Arrow(g,b,20,20,28,28);Box(g,c,12,12,8,8);break;
            case 68: Box(g,b,4,6,24,21);Arrow(g,c,23,16,9,16);break;
            case 69: g.DrawEllipse(b,9,5,14,22);g.DrawEllipse(c,3,11,26,10);Lines(g,a,24,8,28,13,23,16);break;
            case 70: Box(g,b,5,5,22,22);g.DrawLine(c,5,12,27,12);Arrow(g,a,16,23,16,15);break;
            case 71: Box(g,b,5,5,22,22);g.DrawLine(c,5,22,27,22);Arrow(g,a,16,8,16,19);break;
            case 72: Box(g,b,5,5,22,22);g.DrawLine(c,21,5,21,27);Arrow(g,a,8,16,18,16);break;
            case 73: Cube(g,b);Arrow(g,c,16,16,16,5);break;
            case 74: Cube(g,b);Lines(g,c,5,23,16,17,27,23);g.DrawLine(c,16,4,16,17);break;
            case 75: Cube(g,b);using(var fill=new SolidBrush(Color.FromArgb(110,c.Color)))g.FillPolygon(fill,Points(16,16,27,10,27,23,16,29));break;
            case 76: Poly(g,b,3,10,9,10,12,6,21,6,24,10,29,10,29,27,3,27);g.DrawEllipse(c,10,12,13,12);break;
            case 77: Box(g,b,3,5,26,23);Box(g,c,7,9,12,11);Box(g,a,17,18,9,7);break;
            case 78: Arrow(g,b,8,24,8,4);Arrow(g,c,8,24,28,24);Arrow(g,a,8,24,23,9);break;
            case 79: Box(g,b,3,5,26,19);g.DrawLine(c,16,24,16,29);g.DrawLine(c,10,29,22,29);break;
            case 80: Box(g,b,4,7,24,20);g.DrawLine(c,4,14,28,14);g.DrawLine(c,4,21,28,21);g.DrawLine(c,12,7,12,14);g.DrawLine(c,21,14,21,21);g.DrawLine(c,12,21,12,27);break;
            case 81: Lines(g,b,6,28,6,4,25,4,25,28);Poly(g,c,8,6,20,10,20,28,8,26);g.DrawEllipse(a,16,17,2,2);break;
            case 82: Box(g,b,4,5,24,23);g.DrawLine(c,16,5,16,28);g.DrawLine(c,4,16,28,16);break;
            case 83: Lines(g,b,3,28,3,23,9,23,9,17,15,17,15,11,21,11,21,5,28,5);Arrow(g,c,6,16,18,4);break;
            case 84: Poly(g,b,3,20,16,5,29,20);g.DrawLine(c,8,20,8,26);g.DrawLine(c,24,20,24,26);g.DrawLine(c,8,26,24,26);break;
            case 85: Lines(g,b,3,15,16,4,29,15);Lines(g,c,7,14,7,28,25,28,25,14);Box(g,a,13,19,6,9);break;
            case 86: Box(g,b,10,8,12,18);Box(g,c,6,4,20,4);Box(g,c,6,26,20,3);g.DrawLine(b,14,10,14,24);g.DrawLine(b,18,10,18,24);break;
            case 87: Box(g,b,4,6,24,5);Box(g,b,4,23,24,5);Box(g,c,13,11,6,12);break;
            case 88: Box(g,b,12,4,8,14);Box(g,c,8,18,16,5);Box(g,b,3,23,26,6);break;
            case 89: Lines(g,b,4,29,11,3);Lines(g,b,28,29,21,3);g.DrawLine(c,16,5,16,10);g.DrawLine(c,16,14,16,19);g.DrawLine(c,16,23,16,28);break;
            case 90: Lines(g,b,3,25,3,9,29,9,29,25);g.DrawArc(c,6,15,20,22,180,180);g.DrawLine(b,3,14,29,14);g.DrawLine(a,9,5,9,9);g.DrawLine(a,23,5,23,9);break;
            case 91: Lines(g,b,4,26,4,12,12,4,28,4);Lines(g,c,11,26,11,16,16,11,28,11);g.DrawLine(a,3,23,12,23);g.DrawLine(a,24,3,24,12);break;
            case 92: Poly(g,b,4,12,16,20,28,12,28,27,16,20,4,27);g.DrawLine(c,16,20,16,6);g.DrawLine(c,10,6,22,6);break;
            case 93: Lines(g,b,4,25,4,15,20,15,20,22,28,22,28,10,12,10);g.DrawLine(c,12,5,22,5);g.DrawLine(c,17,5,17,10);g.DrawArc(a,22,24,6,6,0,180);break;
            case 94: Box(g,b,5,5,22,22);g.DrawEllipse(c,8,8,16,16);g.DrawLine(a,12,13,12,17);g.DrawLine(a,20,13,20,17);g.DrawLine(c,15,21,17,21);break;
            case 95: g.DrawArc(b,7,4,18,19,150,240);Lines(g,c,9,19,12,24,20,24,23,19);g.DrawLine(a,12,28,20,28);g.DrawLine(c,16,14,16,23);break;
            case 96: Lines(g,b,23,4,18,9,23,14,28,9);g.DrawArc(b,16,3,13,13,0,90);Poly(g,c,18,13,5,25,8,28,21,16);g.DrawEllipse(a,6,24,2,2);break;
            case 97: Poly(g,b,4,8,12,3,24,6,28,12,20,14,16,10,8,14);Poly(g,c,13,12,18,15,10,29,5,26);break;
            case 98: Poly(g,b,21,3,27,9,16,20,10,14);Lines(g,c,12,18,4,26,6,28,14,20);g.DrawLine(a,19,7,23,11);break;
            case 99: Poly(g,b,4,7,21,7,24,16,12,16,12,28,6,28,6,16,4,16);g.DrawLine(c,24,10,29,10);g.DrawLine(c,24,14,29,14);g.DrawLine(a,11,11,16,11);break;
            case 100: for(int i=0;i<8;i++){double t=i*Math.PI/4;g.DrawLine(c,16+10*(float)Math.Cos(t),16+10*(float)Math.Sin(t),16+13*(float)Math.Cos(t),16+13*(float)Math.Sin(t));}g.DrawEllipse(b,6,6,20,20);g.DrawEllipse(c,12,12,8,8);break;
            case 101: Poly(g,b,9,3,23,3,26,8,23,13,9,13,6,8);Box(g,c,12,13,8,15);for(int y=16;y<29;y+=4)g.DrawLine(a,11,y,21,y-2);break;
            case 102: Poly(g,b,10,4,23,4,29,16,23,28,10,28,3,16);g.DrawEllipse(c,10,10,12,12);break;
            case 103: Poly(g,b,4,23,23,4,29,10,10,29);for(int i=0;i<4;i++)g.DrawLine(c,10+i*4,19-i*4,13+i*4,22-i*4);break;
            case 104: g.DrawEllipse(b,13,3,6,6);g.DrawLine(b,16,9,5,28);g.DrawLine(c,16,9,27,28);g.DrawArc(a,5,12,22,13,10,160);break;
            case 105: Poly(g,b,5,27,8,19,24,3,29,8,13,24);g.DrawLine(c,8,19,13,24);g.DrawLine(a,21,6,26,11);break;
            case 106: Poly(g,b,14,18,23,3,28,6,19,22);g.DrawBezier(c,14,18,5,17,13,27,3,28);g.DrawBezier(c,3,28,13,30,19,28,19,22);break;
            case 107: Poly(g,b,4,19,18,4,29,15,16,28,12,28);g.DrawLine(c,12,11,23,22);g.DrawLine(a,16,28,29,28);break;
            case 108: Lines(g,b,5,5,5,19);g.DrawArc(b,5,11,22,18,0,180);g.DrawLine(b,27,19,27,5);g.DrawLine(c,5,11,11,11);g.DrawLine(c,21,11,27,11);Lines(g,c,11,5,11,19);g.DrawArc(c,11,15,10,8,0,180);g.DrawLine(c,21,19,21,5);break;
            case 109: Box(g,b,3,10,26,13);g.DrawEllipse(c,11,12,10,9);g.DrawLine(a,16,14,16,19);g.DrawLine(b,6,14,6,19);g.DrawLine(b,26,14,26,19);break;
            case 110: Box(g,b,3,11,26,17);Lines(g,c,11,11,11,5,21,5,21,11);g.DrawLine(b,3,18,29,18);Box(g,a,13,16,6,5);break;
            case 111: g.DrawArc(b,5,6,22,29,180,180);Lines(g,c,3,23,3,27,29,27,29,23,3,23);g.DrawLine(a,13,6,13,19);g.DrawLine(a,19,6,19,19);break;
            case 112: Poly(g,b,3,8,13,8,16,12,29,12,29,27,3,27);g.DrawLine(c,5,16,27,16);break;
            case 113: g.DrawArc(b,3,13,12,13,90,190);g.DrawArc(b,9,6,16,18,170,210);g.DrawArc(b,20,14,10,12,250,200);g.DrawLine(c,9,26,25,26);break;
            case 114: Arrow(g,c,16,3,16,22);Lines(g,b,4,21,4,28,28,28,28,21);break;
            case 115: Arrow(g,c,16,23,16,4);Lines(g,b,4,21,4,28,28,28,28,21);break;
            case 116: g.DrawArc(b,3,13,18,14,30,290);g.DrawArc(c,12,4,18,14,200,290);g.DrawLine(a,11,21,23,10);break;
            case 117: g.DrawEllipse(b,3,3,26,26);g.DrawEllipse(c,10,3,12,26);g.DrawLine(c,3,16,29,16);g.DrawArc(c,3,5,26,10,0,180);g.DrawArc(c,3,17,26,10,180,180);break;
            case 118: g.DrawArc(b,6,3,20,20,180,180);Lines(g,b,6,13,16,29,26,13);g.DrawEllipse(c,12,9,8,8);break;
            case 119: g.DrawEllipse(b,3,3,26,26);Poly(g,c,22,8,18,19,9,24,13,13);g.DrawLine(a,13,13,18,19);break;
            case 120: g.DrawEllipse(b,3,3,26,26);Lines(g,c,16,7,16,16,23,20);break;
            case 121: Box(g,b,4,7,24,22);g.DrawLine(c,4,14,28,14);g.DrawLine(c,10,3,10,10);g.DrawLine(c,22,3,22,10);Box(g,a,10,19,4,4);g.DrawLine(b,19,21,23,21);break;
            case 122: g.DrawEllipse(b,10,3,12,12);g.DrawArc(c,5,17,22,22,180,180);g.DrawLine(c,5,28,27,28);break;
            case 123: g.DrawEllipse(b,11,3,10,10);g.DrawEllipse(c,2,8,7,7);g.DrawEllipse(c,23,8,7,7);g.DrawArc(b,8,16,16,20,180,180);g.DrawArc(c,1,18,8,16,180,130);g.DrawArc(c,23,18,8,16,230,130);break;
            case 124: Box(g,b,3,7,26,20);Lines(g,c,3,8,16,19,29,8);g.DrawLine(b,3,27,11,19);g.DrawLine(b,29,27,21,19);break;
            case 125: Poly(g,b,5,4,11,4,14,11,10,14,18,22,21,18,28,21,28,27,23,29,14,25,7,18,3,9);break;
            case 126: g.DrawEllipse(c,3,22,8,6);g.DrawEllipse(c,21,18,8,6);Lines(g,b,11,24,11,7,29,3,29,20);g.DrawLine(b,11,12,29,8);break;
            case 127: g.DrawBezier(b,16,28,-8,12,8,-1,16,10);g.DrawBezier(c,16,10,24,-1,40,12,16,28);break;
            case 128:
                Poly(g,b,3,9,10,5,17,9,10,13);Lines(g,b,10,13,10,19,17,15,17,9);
                Arrow(g,a,19,7,27,13);Box(g,c,14,21,15,7);g.DrawLine(b,21,21,21,28);break;
            case 129: Lines(g,b,4,24,14,14,21,21,28,14);Lines(g,c,5,5,15,15);for(int n=0;n<5;n++){double t=n*1.2;g.DrawLine(a,19,10,19+5*(float)Math.Cos(t),10+5*(float)Math.Sin(t));}break;
            case 130: Box(g,b,3,20,26,8);Lines(g,c,12,3,20,3,20,8,16,12,12,8,12,3);g.DrawLine(a,16,13,16,25);break;
            case 131: Lines(g,b,3,25,15,25,15,9,28,9);Arrow(g,c,6,6,11,15);g.DrawArc(a,18,13,9,9,0,180);break;
            case 132: Box(g,b,3,3,26,26);Poly(g,c,6,6,16,6,16,13,10,13,10,20,6,20);Box(g,a,19,6,7,14);Box(g,c,13,23,13,3);break;
            case 133: g.DrawEllipse(c,12,3,8,7);Poly(g,b,9,10,23,10,29,28,3,28);Lines(g,a,11,16,11,24,15,20,18,24,18,16);break;
            case 134: Box(g,b,5,5,22,22);for(int n=9;n<27;n+=5)g.DrawLine(c,5,n,27,n);Arrow(g,a,2,27,2,5);break;
            case 135: Cube(g,b);g.DrawLine(c,5,23,16,16);g.DrawLine(c,27,23,16,16);g.DrawEllipse(a,12,9,8,5);break;
            case 136: Lines(g,b,3,3,3,29,29,29);Box(g,c,7,18,4,7);Box(g,c,15,12,4,13);Box(g,a,23,5,4,20);break;
            case 137: g.DrawEllipse(b,5,3,22,8);Lines(g,b,5,7,5,25);Lines(g,b,27,7,27,25);g.DrawArc(b,5,21,22,8,0,180);g.DrawArc(c,5,11,22,8,0,180);g.DrawArc(c,5,17,22,8,0,180);break;
            case 138: Page(g,b);Lines(g,c,13,13,9,17,13,21);Lines(g,c,21,13,25,17,21,21);g.DrawLine(a,19,12,16,23);break;
            case 139: Lines(g,b,9,3,9,10,23,10,23,3);Box(g,c,5,10,22,10);Lines(g,b,10,20,10,25,22,25,22,20);g.DrawLine(a,16,25,16,30);break;
            case 140: Page(g,b);Lines(g,c,10,18,15,23,24,13);break;
            case 141: g.DrawLine(b,11,4,7,28);g.DrawLine(b,23,4,19,28);g.DrawLine(c,4,11,29,11);g.DrawLine(a,3,22,28,22);break;
            case 142: Box(g,b,3,3,10,10);Box(g,c,19,3,10,10);Box(g,c,3,19,10,10);Box(g,a,20,20,4,4);Lines(g,b,28,17,28,28,18,28);break;
            case 143: for(int n=4;n<29;n+=3)g.DrawLine(n%2==0?b:c,n,5,n,n%2==0?27:23);break;
            case 144: Box(g,b,3,5,26,22);g.DrawEllipse(a,7,8,5,5);Poly(g,c,4,25,13,16,18,21,23,14,28,25);break;
            case 145: Page(g,b);for(int n=14;n<27;n+=4)g.DrawLine(c,11,n,22,n);break;
            case 146: g.DrawArc(b,4,4,24,24,205,145);g.DrawArc(c,4,4,24,24,25,145);Lines(g,a,20,2,27,5,27,12);Lines(g,a,12,30,5,27,5,20);break;
            case 147: Box(g,b,3,12,17,16);g.DrawArc(c,10,3,19,19,160,240);Lines(g,a,25,4,28,10,21,10);g.DrawLine(c,7,22,16,22);break;
            case 148: Box(g,b,5,14,22,15);g.DrawArc(c,9,2,14,20,180,180);g.DrawLine(a,16,19,16,24);break;
            case 149: Box(g,b,5,14,22,15);g.DrawArc(c,14,2,14,20,180,210);g.DrawLine(a,16,19,16,24);break;
            case 150: Poly(g,b,3,4,29,4,19,16,19,26,13,29,13,16);g.DrawLine(c,8,9,24,9);break;
            case 151: Arrow(g,b,6,4,6,28);g.DrawLine(c,14,7,20,7);g.DrawLine(c,14,16,24,16);g.DrawLine(a,14,25,29,25);break;
            case 152: Box(g,b,3,4,10,24);Box(g,c,19,4,10,24);Arrow(g,a,10,16,23,16);g.DrawLine(b,6,9,10,9);g.DrawLine(c,22,23,26,23);break;
            case 153: Lines(g,c,6,7,25,7,16,25,6,7);g.DrawEllipse(b,2,3,8,8);g.DrawEllipse(b,21,3,8,8);g.DrawEllipse(a,12,21,8,8);break;
            case 154: Lines(g,b,5,25,5,15,17,15,17,5,27,5);Arrow(g,a,23,5,28,5);g.DrawEllipse(c,2,22,6,6);break;
            case 155: Lines(g,b,2,16,8,16,10,9,14,23,18,9,22,23,24,16,30,16);g.DrawLine(c,5,4,27,4);g.DrawLine(a,16,2,16,6);break;
            case 156: g.DrawEllipse(b,3,3,26,26);g.DrawEllipse(c,11,11,10,10);for(int n=0;n<6;n++){double t=n*Math.PI/3;g.DrawEllipse(a,14+9*(float)Math.Cos(t),14+9*(float)Math.Sin(t),4,4);}break;
            case 157: Lines(g,b,4,3,16,3,16,6,8,9,24,13,8,17,24,21,16,25,16,29,28,29);break;
            case 158: Box(g,b,5,9,22,18);g.DrawLine(c,16,4,16,9);g.DrawEllipse(a,14,1,4,4);Box(g,c,9,14,4,4);Box(g,c,20,14,4,4);g.DrawLine(a,11,23,21,23);break;
            case 159: Star(g,b,16,16,14);Star(g,c,16,16,6);break;
            case 160: g.DrawEllipse(b,3,3,8,8);g.DrawEllipse(b,21,3,8,8);g.DrawEllipse(c,4,7,24,22);g.DrawEllipse(b,8,12,6,8);g.DrawEllipse(b,18,12,6,8);g.DrawEllipse(a,14,21,4,3);break;
        }
    }
}
