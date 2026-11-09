#if ZWCAD
using ZwSoft.ZwCAD.Runtime;
#else
using Autodesk.AutoCAD.Runtime;
#endif
using System.Runtime.CompilerServices;

[assembly: ExtensionApplication(typeof(XiaoYang.AutoCADPlugin.CadPlugin))]
[assembly: CommandClass(typeof(XiaoYang.AutoCADPlugin.CadPlugin))]
[assembly: InternalsVisibleTo("UiPreview")]
