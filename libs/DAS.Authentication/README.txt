DAS.Authentication (binary reference)
=====================================
Copied from DAS.Shared.Authentication Release build (no source changes).

Includes:
  DAS.Authentication.dll
  DAS.Repository.dll
  DAS.Core.Shared.dll
  DAS.Models.dll

NuGet packages required by consumers (match IdentityModel 8.x):
  Microsoft.IdentityModel.Protocols.OpenIdConnect
  Microsoft.IdentityModel.Tokens
  System.DirectoryServices.AccountManagement (Windows / AD providers)

OIDC / JWT consumers do not need a live DAS database; Repository DLL
is shipped only because Authentication was built against it.
