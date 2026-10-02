import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const skill = 'C:/Users/felip/OneDrive/Documentos/OneDrive - AUTVIX ENGENHARIA CONSULTORIA LTDA/CodexCompartilhado/.codex/plugins/cache/openai-primary-runtime/presentations/26.930.11008/skills/presentations';
const workspaceDir = 'C:/Users/felip/OneDrive/Documentos/myPKA/Deliverables/projects/2026-07-14-modbus-tcp-troubleshooter/training';
const { finalizePresentation } = await import(pathToFileURL(path.join(skill,'container_tools/artifact_tool_utils.mjs')).href);
const slides=JSON.parse(await fs.readFile(path.join(workspaceDir,'.build/slides.json'),'utf8'));
const tableOwners=slides.flatMap((s,i)=>s.kind==='table'?[i+1]:[]);
const result=await finalizePresentation({
  workspaceDir,
  candidatePath:path.join(workspaceDir,'.build/candidate.pptx'),
  finalPath:path.join(workspaceDir,'output/Treinamento-Modbus-TCP-2026-10-03-final.pptx'),
  pythonExecutable:'C:/Users/felip/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',
  integrityValidatorPath:path.join(skill,'container_tools/inspect_presentation_package_integrity.py'),
  layoutValidatorPath:path.join(skill,'container_tools/inspect_presentation_layout_geometry.py'),
  layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-heading-fit',...tableOwners.flatMap(n=>['--require-native-table-slide',String(n)])],
  requiredNativeTableOwnerSlides:tableOwners,
  requiredNativeChartOwnerSlides:[22],
  fontPolicy:{basis:'design',families:['Segoe UI']},
  verifyArtifactToolImport:true,
  receiptPath:path.join(workspaceDir,'.build/final-validation.json'),
});
console.log(JSON.stringify(result,null,2));
