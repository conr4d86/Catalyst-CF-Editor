using System;
using System.Windows.Forms;

// UI-only host mode. The real USB count stays zero; no fake device is created.
partial class OneClickApp {
 bool embeddedPreview;
 public void ConfigureEmbeddedPreview(){
  if(!preview)throw new InvalidOperationException("La vista de diseño debe estar aislada antes de abrirla");
  embeddedPreview=true;RefreshUI();
 }
 void RefreshEmbeddedPreview(){
  if(!embeddedPreview)return;
  workspace.Enabled=!busy;
  run.Enabled=false;
  run.Text="Instalación desactivada · vista de diseño";
  connection.Text="Vista de diseño · sin USB";
 }
}
