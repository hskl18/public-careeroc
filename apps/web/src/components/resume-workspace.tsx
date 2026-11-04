"use client";

import { useState } from "react";
import dynamic from "next/dynamic";
import type {
  CorrectResumeFieldRequest,
  CreateResumeRequest,
  ImportResumeFileRequest,
  PatchResumeStructuredDataRequest,
  ResumeStructuredDataPatchOperation,
  ResumeWorkspace,
} from "@/lib/resumes";

const ResumeDossier = dynamic(() =>
  import("./resume-dossier").then((module) => module.ResumeDossier),
);
const ResumeImportPanel = dynamic(() =>
  import("./resume-import-panel").then((module) => module.ResumeImportPanel),
);

type ResumePendingOperation = "file-import" | "text-create" | "reanalyze" | "reextract" | null;
type ResumeImportPhase = "reading" | "saving" | "extracting" | "analyzing" | null;

type ResumeWorkspaceProps = {
  initialWorkspace: ResumeWorkspace;
  source: "api" | "fallback";
  proxyPrefix: string | null;
  currentUserEmail: string;
  currentUserName?: string | null;
  applicationId?: string | null;
};

async function readErrorMessage(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : "";
      const commaIndex = result.indexOf(",");
      resolve(commaIndex >= 0 ? result.slice(commaIndex + 1) : result);
    };
    reader.onerror = () => reject(reader.error ?? new Error("File read failed."));
    reader.readAsDataURL(file);
  });
}

function getResumeTitleFromFileName(fileName: string) {
  return fileName.replace(/\.[^/.]+$/, "").replace(/[_-]+/g, " ").trim();
}

export function ResumeWorkspace({
  initialWorkspace,
  source,
  proxyPrefix,
  applicationId,
}: ResumeWorkspaceProps) {
  const [workspace, setWorkspace] = useState(initialWorkspace);
  const [title, setTitle] = useState("");
  const [sourceText, setSourceText] = useState("");
  const [importFile, setImportFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [isImporting, setIsImporting] = useState(false);
  const [pendingOperation, setPendingOperation] = useState<ResumePendingOperation>(null);
  const [importPhase, setImportPhase] = useState<ResumeImportPhase>(null);
  const [correctionFieldPath, setCorrectionFieldPath] = useState("summary");
  const [correctionValue, setCorrectionValue] = useState("");
  const [correctionNote, setCorrectionNote] = useState("");
  const [isCorrecting, setIsCorrecting] = useState(false);
  const [showNewResumeForm, setShowNewResumeForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const canPersist = Boolean(proxyPrefix);
  const isResumeBusy = pendingOperation !== null || isImporting || isCorrecting;
  const canImport = canPersist && Boolean(importFile) && !isResumeBusy;
  const normalizedSourceText = sourceText.trim();
  const canCreateFromText =
    canPersist && normalizedSourceText.length >= 80 && !isResumeBusy;
  const workspaceLoadWarning =
    source === "fallback" && canPersist
      ? "Resume data could not be loaded yet. Saving will retry the live API."
      : null;
  const latestDocument = workspace.documents[0] ?? null;
  const hasResumeData = workspace.documents.length > 0;
  async function importResumeFile() {
    if (!proxyPrefix || !canImport || !importFile) return;

    setIsImporting(true);
    setPendingOperation("file-import");
    setImportPhase("reading");
    setError(null);
    setNotice(null);

    try {
      const base64Content = await readFileAsBase64(importFile);
      setImportPhase("extracting");
      const request: ImportResumeFileRequest = {
        title: title.trim() || null,
        fileName: importFile.name,
        contentType: importFile.type || "application/octet-stream",
        base64Content,
      };
      const response = await fetch(`${proxyPrefix}/api/resumes/import`, {
        method: "POST",
        headers: {
          "content-type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      setImportPhase("analyzing");
      setWorkspace((await response.json()) as ResumeWorkspace);
      setImportFile(null);
      setFileInputKey((key) => key + 1);
      setTitle("");
      setShowNewResumeForm(false);
      setNotice("Resume file imported and evaluated.");
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "Resume import failed.");
    } finally {
      setIsImporting(false);
      setPendingOperation(null);
      setImportPhase(null);
    }
  }

  async function createResumeFromText() {
    if (!proxyPrefix || !canPersist) return;

    const resumeText = sourceText.trim();
    if (resumeText.length < 80) {
      setError("Paste at least 80 characters of resume text before saving.");
      setNotice(null);
      return;
    }

    setIsImporting(true);
    setPendingOperation("text-create");
    setImportPhase("saving");
    setError(null);
    setNotice(null);

    try {
      const request: CreateResumeRequest = {
        title: title.trim() || null,
        sourceKind: "text",
        sourceText: resumeText,
        sourceFileName: null,
      };
      const response = await fetch(`${proxyPrefix}/api/resumes`, {
        method: "POST",
        headers: {
          "content-type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      setImportPhase("analyzing");
      setWorkspace((await response.json()) as ResumeWorkspace);
      setSourceText("");
      setImportFile(null);
      setFileInputKey((key) => key + 1);
      setTitle("");
      setShowNewResumeForm(false);
      setNotice("Resume text saved and evaluated.");
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : "Resume save failed.");
    } finally {
      setIsImporting(false);
      setPendingOperation(null);
      setImportPhase(null);
    }
  }

  function selectImportFile(file: File | null) {
    setImportFile(file);
    if (!file) return;

    setTitle(getResumeTitleFromFileName(file.name) || file.name);
  }

  async function submitPatch(
    versionId: string,
    operations: ResumeStructuredDataPatchOperation[],
    note: string | null = null,
  ): Promise<boolean> {
    if (!proxyPrefix || !canPersist || operations.length === 0) return false;
    setError(null);
    setNotice(null);
    try {
      const request: PatchResumeStructuredDataRequest = { operations, note };
      const response = await fetch(
        `${proxyPrefix}/api/resumes/versions/${versionId}/structured-data`,
        {
          method: "PATCH",
          headers: { "content-type": "application/json" },
          body: JSON.stringify(request),
        },
      );
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      setWorkspace((await response.json()) as ResumeWorkspace);
      setNotice("Field saved.");
      return true;
    } catch (patchError) {
      setError(patchError instanceof Error ? patchError.message : "Save failed.");
      return false;
    }
  }

  async function submitCorrection(versionId: string) {
    if (!proxyPrefix || !canPersist || correctionValue.trim().length === 0) return;

    const request: CorrectResumeFieldRequest = {
      fieldPath: correctionFieldPath.trim(),
      previousValue: null,
      correctedValue: correctionValue,
      note: correctionNote.trim() || null,
    };

    setIsCorrecting(true);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/resumes/versions/${versionId}/corrections`,
        {
          method: "POST",
          headers: {
            "content-type": "application/json",
          },
          body: JSON.stringify(request),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      setWorkspace((await response.json()) as ResumeWorkspace);
      setCorrectionValue("");
      setCorrectionNote("");
      setNotice("Correction saved.");
    } catch (correctionError) {
      setError(correctionError instanceof Error ? correctionError.message : "Correction save failed.");
    } finally {
      setIsCorrecting(false);
    }
  }

  async function analyzeResumeVersion(versionId: string) {
    if (!proxyPrefix || !canPersist || isResumeBusy) return;

    setPendingOperation("reanalyze");
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/resumes/versions/${versionId}/analyze`,
        {
          method: "POST",
        },
      );
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      setWorkspace((await response.json()) as ResumeWorkspace);
      setNotice("Resume review re-run.");
    } catch (analyzeError) {
      setError(analyzeError instanceof Error ? analyzeError.message : "Resume review failed.");
    } finally {
      setPendingOperation(null);
    }
  }

  async function reextractResumeDocument(documentId: string) {
    if (!proxyPrefix || !canPersist || isResumeBusy) return;

    setPendingOperation("reextract");
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/resumes/${documentId}/reextract`, {
        method: "POST",
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      setWorkspace((await response.json()) as ResumeWorkspace);
      setNotice("Resume re-extracted and reviewed.");
    } catch (reextractError) {
      setError(reextractError instanceof Error ? reextractError.message : "Resume re-extract failed.");
    } finally {
      setPendingOperation(null);
    }
  }

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col px-4 py-5 animate-in sm:px-5 sm:py-6 md:h-full md:overflow-hidden">
        {!hasResumeData ? (
          <section className="card app-workspace-panel flex min-h-0 flex-1 flex-col items-center justify-center overflow-hidden px-5 py-8 text-center">
            <ResumeImportPanel
              inputId="resume-empty-upload"
              fileInputKey={fileInputKey}
              importFile={importFile}
              title={title}
              canPersist={canPersist}
              canImport={canImport}
              canCreateFromText={canCreateFromText}
              isImporting={isImporting}
              phase={importPhase}
              notice={notice}
              error={error}
              warning={workspaceLoadWarning}
              sourceText={sourceText}
              onFileChange={selectImportFile}
              onTitleChange={setTitle}
              onSourceTextChange={setSourceText}
              onImport={importResumeFile}
              onCreateFromText={createResumeFromText}
            />
          </section>
        ) : (
          <ResumeDossier
            document={latestDocument}
            documents={workspace.documents}
            canPersist={canPersist}
            showNewResumeForm={showNewResumeForm}
            title={title}
            sourceText={sourceText}
            importFile={importFile}
            fileInputKey={fileInputKey}
            canImport={canImport}
            canCreateFromText={canCreateFromText}
            isImporting={isImporting}
            pendingOperation={pendingOperation}
            importPhase={importPhase}
            fieldPath={correctionFieldPath}
            correctedValue={correctionValue}
            note={correctionNote}
            isCorrecting={isCorrecting}
            notice={notice}
            error={error}
            onToggleNewResumeForm={() => setShowNewResumeForm((value) => !value)}
            onFileChange={selectImportFile}
            onTitleChange={setTitle}
            onSourceTextChange={setSourceText}
            onImport={importResumeFile}
            onCreateFromText={createResumeFromText}
            onFieldPathChange={setCorrectionFieldPath}
            onCorrectedValueChange={setCorrectionValue}
            onNoteChange={setCorrectionNote}
            onSubmit={submitCorrection}
            onPatch={submitPatch}
            onAnalyze={analyzeResumeVersion}
            onReextract={reextractResumeDocument}
            proxyPrefix={proxyPrefix}
            applicationId={applicationId}
          />
        )}
      </div>
    </main>
  );
}
