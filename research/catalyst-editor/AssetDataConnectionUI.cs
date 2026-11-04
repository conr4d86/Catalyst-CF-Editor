using System;using System.IO;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace CFEditor {
sealed partial class AssetDataPanel {
 readonly ActionButton receiverConnect=new ActionButton();AssetDataSession dataSession;Action<Exception> sessionFailure;Func<AssetDataSession> createSession;uint verifiedProduct;
 internal void ConfigureConnection(uint product,Func<AssetDataSession> factory){verifiedProduct=product;createSession=factory;Render();}
 void BuildConnectionAction(){
  receiverConnect.Text="2. Conectar al ampli";receiverConnect.Font=FirmwareText.Bold;card.Controls.Add(receiverConnect);
  receiverConnect.Click+=async delegate{await ConnectionAction();};
 }
 void RenderConnectionAction(){
  receiverConnect.Visible=!initialMode;
  receiverConnect.Text=dataSession!=null&&dataSession.HasOwner?(dataSession.Ready?"2. Desconectar del ampli":"Cerrar conexión de datos"):"2. Conectar al ampli";
  receiverConnect.Enabled=!preview&&active&&!Busy&&createSession!=null;receiverConnect.Invalidate();
 }
 internal async Task ConnectionAction(){
  if(preview||!active||Busy||createSession==null)return;
  if(dataSession!=null&&dataSession.HasOwner){try{await CloseConnection();}catch(Exception error){Note(error.Message,true);Render();}return;}
  if(!DeviceFamily.Supported((int)verifiedProduct)){Note("Conecta primero desde SONIDO para comprobar el modelo del amplificador.",true);Render();return;}
  DropReceiver();operationError=null;connecting=true;Render();Exception failure=null;
  try{
   dataSession=createSession();if(dataSession==null)throw new IOException("No se ha preparado el propietario de la conexión.");
   var current=dataSession;sessionFailure=error=>ConnectionFailed(current,error);dataSession.Failed+=sessionFailure;receiverText="Comprobando el receptor de datos…";Render();
   await dataSession.Connect(verifiedProduct,Nonce(),CancellationToken.None);
   await AdoptReceiver(dataSession.Client,CancellationToken.None);
   ConfirmInitialReceiver();if(ReceiverVerified!=null)ReceiverVerified(verifiedProduct,dataSession.VerifiedIdentity);
   if(initialMode&&initialRecord!=null&&initialRecord.Phase=="active"){await ReleaseSession();DropReceiver();knownReceiver=true;receiverText="✓ Custom Firmware instalado y comprobado";}
  }catch(Exception error){failure=error;}
  if(failure!=null){
   DropReceiver();try{await ReleaseSession();}catch(Exception cleanup){failure=new IOException(failure.Message+" "+cleanup.Message,cleanup);}
   Note(failure.Message,true);
  }
  connecting=false;Render();
 }
 void ConnectionFailed(AssetDataSession source,Exception error){
  if(disposed||!ReferenceEquals(source,dataSession))return;
  try{BeginInvoke((Action)delegate{if(disposed||!ReferenceEquals(source,dataSession))return;DropReceiver();if(ReceiverLost!=null)ReceiverLost();Note("La conexión de datos se ha interrumpido. Ciérrala antes de volver a conectar.",true);Render();});}catch(InvalidOperationException){}
 }
 async Task ReleaseSession(){
  if(dataSession==null)return;await dataSession.Close();dataSession.Failed-=sessionFailure;sessionFailure=null;dataSession=null;
 }
 internal async Task CloseConnection(){
  if(Busy)throw new InvalidOperationException("Espera a que termine la operación de datos.");
  DropReceiver();connecting=true;Render();
  try{await ReleaseSession();receiverText="Conexión de datos cerrada";}finally{connecting=false;Render();}
 }
 internal bool HasDataOwner{get{return dataSession!=null&&dataSession.HasOwner;}}
}
}
