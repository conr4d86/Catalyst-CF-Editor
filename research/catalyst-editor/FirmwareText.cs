using System.Drawing;
namespace CFEditor {
// Larger typography is scoped to Firmware CF; editor dials and pages keep
// their existing sizes. EditorZoom applies the same proportional scale once.
static class FirmwareText {
 public static readonly Font Body=new Font("Segoe UI",13f);
 public static readonly Font Small=new Font("Segoe UI",11.5f);
 public static readonly Font Bold=new Font("Segoe UI",13f,FontStyle.Bold);
 public static readonly Font Action=new Font("Segoe UI",14.5f,FontStyle.Bold);
 public static readonly Font Title=new Font("Segoe UI",19f,FontStyle.Bold);
 public static readonly Font Log=new Font("Consolas",11.5f);
}
}
