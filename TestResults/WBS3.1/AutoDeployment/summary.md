# Automatic board deployment verification

2026-10-06

Before manual or automatic combat start, deploy bench units into free preparation board cells
up to the remaining level-based deployment capacity. Existing board positions are preserved.
Selection order: ascending bench slot. Destination order: ascending row, then column.
Applied to both players. Persist owned placements through Result.

Plan and revision preflight for both players precede battle creation.
Commit placements only after successful battle snapshot/simulation construction.
Revision increases once per player whose placement changes.
No Gold, items, shop, pool or RNG changes caused by deployment.

219/219 related EditMode tests passed:
- AutomaticBoardDeploymentTests: 10
- LocalRoundCoordinatorTests: 17
- RoundFlowControllerTests: 31
- PlayerPlacementTests: 43
- PlayerBoardTests: 32
- MatchCommandGatewayTests: 8
- RankUpTests: 41
- ShopTransactionTests: 34
- AssemblyBoundaryTests: 3

UI VerifyAutomaticDeployment passed: pointer purchases begin on bench;
manual and timer starts deploy the first two bench units at Level 2;
third remains on bench; drag cancels; battle snapshot and Result tokens match.
1280x720 combat/result captures inspected.

Windows Development build Succeeded: errors 0, warnings 2.
Build time: 15.441s.
Output: Builds/WBS3.1/PokeChessRoundTimer.exe
Warnings:
- Pipeline: No RuntimePipelineConfig asset found (Project Settings > Pipeline > Runtime). Pipeline will be disabled in Player builds.
- Exception occurred attempting to connect to Unity services. Native symbols will not be uploaded for this build. Exception details:
System.UriFormatException: Invalid URI: The URI is empty.
  at System.Uri.CreateThis (System.String uri, System.Boolean dontEscape, System.UriKind uriKind) [0x0007b] in <21669058eef547deb03cf22e8499ee91>:0 
  at System.Uri..ctor (System.String uriString) [0x00014] in <21669058eef547deb03cf22e8499ee91>:0 
  at System.Net.WebRequest.Create (System.String requestUriString) [0x0000e] in <21669058eef547deb03cf22e8499ee91>:0 
  at UnityEditor.CrashReporting.CrashReporting.GetUsymUploadAuthToken () [0x000a9] in <49fcb209645d4ccfb3e373da32345d37>:0 
  at System.Uri.CreateThis (System.String uri, System.Boolean dontEscape, System.UriKind uriKind) [0x0007b] in <21669058eef547deb03cf22e8499ee91>:0 
  at System.Uri..ctor (System.String uriString) [0x00014] in <21669058eef547deb03cf22e8499ee91>:0 
  at System.Net.WebRequest.Create (System.String requestUriString) [0x0000e] in <21669058eef547deb03cf22e8499ee91>:0 
  at UnityEditor.CrashReporting.CrashReporting.GetUsymUploadAuthToken () [0x000a9] in <49fcb209645d4ccfb3e373da32345d37>:0 

The original full 573-test run in the parent folder predates this extension.
This extension was validated with the 219 relevant tests above.
Editor Play mode was stopped after verification. Source WBS Excel was not edited.

