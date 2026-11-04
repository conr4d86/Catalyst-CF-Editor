// Set this after the repository and its Releases page have actually been created.
// The credit stays visible, with no invented or broken hyperlink in the meantime.
static class ProjectLinks {
 public const string LatestReleaseUrl="https://github.com/conr4d86/CatalystIR/releases";
 public static string ValidatedLatestRelease(){
  System.Uri uri;
  if(!System.Uri.TryCreate(LatestReleaseUrl,System.UriKind.Absolute,out uri))return null;
  return uri.Scheme=="https"&&uri.Host=="github.com"&&(uri.AbsolutePath.EndsWith("/releases/latest",System.StringComparison.Ordinal)||uri.AbsolutePath.EndsWith("/releases",System.StringComparison.Ordinal))?uri.AbsoluteUri:null;
 }
}
