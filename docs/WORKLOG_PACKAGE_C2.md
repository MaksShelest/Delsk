# C2 — восстановление
Автор: Шелестов Максим Евгеньевич,25.10.2026.
Base9220b4c/PR61merged, clean; branchfeat/vnext-package-c2. ScopeC2only, noD1.
Env /workspace/work/delsk-tablets-v1/env.sh, evidence /workspace/work/delsk-c2.
Rules typed BALANCE:6000bp,3000minor/cell,+1500/extra cell level. C1 reused.
Design: quote from real ConstructionMaterialConsumed journal; initial free assets
return0 (no fictional paid inputs). Retain retired Building/Enterprise records for
historical market/delivery refs and removable logistics assets; hide active views,
release geometry, normalstock to owned ground normal+overflow, never reset existing
risk deadline. Retained outputinventory emptied/cap0, input identity becomesground.
Preparing stops new batch/assignments, existing WIP/accepted freight can finish;
confirmed freshquote/hash + UIhold2sec; no Corewallclock. Schema20 limited19/18.
Flow.ConstructionRecovered separate fromProduced; demand/capital/conservation updated.
Build/progress only, no tests yet. Need finish build/test then UI/native/gates.
Actual code files Demolition*.cs; Coreexecute commands appended. Optional balance
Demolition rules overrides; standard rules enable onlyStorage/C1. OldC1 configs
retain canonical bytes/hash until firstC2action. No new D content/import added.
Next: meaningfultests, addguard against ghost assignments/market/postretire logic,
validatejournal/stockcount receipts, parkedtruck UI, quote/hold/confirmation native,
save/replay C1compat fullgate/report/commit/push/PR. C2 autoD1 notallowed.

Stage update:13/13C2testsPASS. Core C2quote/holdUI implemented, firstnative failed
nullable RunArtifacts export (fixed). Fullgate headless2 nowrunning but likelyneeds
rerun after integrationfix. Mainimportant: C2 mustwork on legacyordinary factories,
notonly C1newmapwithoutD. AddedHasGroundStorage runtime afterfirstdemolition,
GroundRules fallback70/1200; config.Storage staysnull so legacywarehouse/prod/buy
semanticsunchanged. Snapshot/hashes onlyincludeStorage afterexplicitC2 effects.
LegacyC2testadded (14total). Needvalidate changes/build. NoD1.
Preserved truck can use existingexplicit SetTransportRoute from releasedhome;
PlayerTablet nowshows retainedtruck RouteEditor, no secondfleetexecutor.
Remaining: focused14, freezeCore, fullgate; nativeDemolitionProbe pause/cancel/hold,
runnerScenario/CLIallowlist fix trailingstorage-c1 case, directC2gate+savecuts/replay,
privacy/receiptstrictness, source/C1regression, Godot/Developer, docs/commit/push/PR.

Core freeze:17 C2tests после добавленного запрета новых обязательств defaultSource
preparingShop; serial final suite1094PASS. Старый полный1093PASS, parallel1094timed
out120 (не считатьзелёным), serial1094сновапрошёл109.9s. Serial headless runner
ещё running. C2process16checks/10exports/9pairs/1996ticksPASS.
Native4PASS: previewCancel/noCharge, shortHoldnoCommand,2secHoldoneCommand/paused,
actualfee90/recovery18wood1steel, diskContinue/ground37/70, RU/EN800/1100.
Hold использует BaseButtonDown/Up/MouseExited/windowfocus signals; UI sourceglobal
Inputpoll ненадёжен для native events, заменён eventlogic. Probe warps realcursor.
Developer+rootClient/AssemblyAudit/Godot сейчасrunning /workspace/work/delsk-c2.
Docs usage/ARCH/DECISIONS added, CI C2step added. Ещё отчёт/evidence/AGENTS/plan,
C1backcompatactualoldsave, finalgitreview/commit/push/PR, noD1.

Final review: retired Building/Enterprise metadata must be partofcanonicalHash too
(notonlyreceipts/activeviews). Added conditional RetiredBuildings+RetiredEnterprises
underC2 hashwrapper; ordinary legacy/C1 hashesunchanged. Tamperedretiredleveltestadded.
Canonical suite1095PASS/18newC2, runnerfullgate stillrunning. Also filtered retired
buildingpositions fromaccess-check anchors (walls alreadyexcluded); no ghostblockage.
Developerfirstattempt timedout180 underparallelheavyload; must rerun isolated,
notcountpass. RootGodot1132hashes/35restoresPASS onpre-canonicalCore (rerunfinal).
Latestnative4PASS, UIsource hold signals/windowfocus. Help textC2added.
Need finish now: canonicalrunnerresult, finalsmallCorebuild+focused, C2process/hash
withnewcanonical, Developerisolated, finalclient+Godot/audit, oldC1real save, docsreport/
evidence+AGENTS/plan, commit/push/PR. No furtherfeatures/noD1.

Final exactCore source: corrected partial-normal upgrade-cancel OverflowCreated.quantity
(now actual excess, not whole recovered lot); existing theory upgraded with fullstore
176grain then20newgrain so recovery4normal+8overflowplank/2steel. All1095ordinary
and1101Developer latestPASS. Fastpath ValidateDemolition skips needless events×items
scan on normal legacy ticks, still restore-checks impossible recovery. No new C2 scope.
C2process-final16/10/9/1996 PASS; C1regression24checksPASS; actualold19tick37exacthash
04a13e91946ec4765efb31b35da85e3ffc31a3822a8cd124f952054d88eae764 PASS;37PythonPASS.
RootexactGodot import/smoke/auditPASS. NativefinalPASS, extra presentation polish
localizes steel/beam/concrete and clarifies empty stocks/noautoRestart; native-polish
andclient-polish/audit currentlyrunning. Afterthem: evidence JSON+screens, reportcounts,
remove3import-onlyUIDs, gitdiffreview/commit/push/onePRmain. Sourcebase9220unchanged
remotechecked14:04UTC. NoD1. All paths /workspace/work/delsk-c2.

FINAL: all latest gatesPASS. Exact1095ordinary/1101Developer/37Python,
canonicalfullheadless102/14/69; C2process16/10/9/1996; C1process24PASS;
realold19tick37exacthashPASS; rootGodot1132/35/stopped10PASS; finalnative-polish
RU/EN/hold/diskContinuePASS; rootclient-polish/auditPASS. Evidence+2screens
verification/demolition-c2/checks.json; report+usage+ARCH/DECISIONS+AGENTS/planupdated.
Nextonlygitreview/commit/push/onePRmain9220,no merge/noD1. No morecodechanges.
