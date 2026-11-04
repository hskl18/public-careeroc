"use client";

import { useState } from "react";
import { SvgAsset, mascotAssets } from "./svg-asset";

const acceptedResumeFileTypes = [
  "application/pdf",
  "text/plain",
  ".txt",
  ".md",
  "image/png",
  "image/jpeg",
  "image/webp",
].join(",");

function formatFileSize(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function describeFileKind(file: File) {
  const type = file.type.toLowerCase();
  if (type === "application/pdf") return "PDF";
  if (type.startsWith("image/")) return "Image · vision extraction";
  if (type === "text/markdown" || file.name.toLowerCase().endsWith(".md"))
    return "Markdown";
  if (type === "text/plain" || file.name.toLowerCase().endsWith(".txt"))
    return "Plain text";
  return type || "Unknown type";
}

export function ResumeImportPanel({
  inputId,
  fileInputKey,
  importFile,
  title,
  sourceText,
  canPersist,
  canImport,
  canCreateFromText,
  isImporting,
  phase,
  notice,
  error,
  warning,
  compact = false,
  onFileChange,
  onTitleChange,
  onSourceTextChange,
  onImport,
  onCreateFromText,
}: {
  inputId: string;
  fileInputKey: number;
  importFile: File | null;
  title: string;
  sourceText: string;
  canPersist: boolean;
  canImport: boolean;
  canCreateFromText: boolean;
  isImporting: boolean;
  phase?: "reading" | "saving" | "extracting" | "analyzing" | null;
  notice: string | null;
  error: string | null;
  warning?: string | null;
  compact?: boolean;
  onFileChange: (file: File | null) => void;
  onTitleChange: (value: string) => void;
  onSourceTextChange: (value: string) => void;
  onImport: () => Promise<void>;
  onCreateFromText: () => Promise<void>;
}) {
  const [isDragging, setIsDragging] = useState(false);
  const [mode, setMode] = useState<"file" | "text">("file");
  const dropDisabled = isImporting || !canPersist;
  const isFileMode = mode === "file";
  const trimmedSourceLength = sourceText.trim().length;

  function handleDragOver(event: React.DragEvent<HTMLLabelElement>) {
    if (dropDisabled) return;
    event.preventDefault();
    setIsDragging(true);
  }

  function handleDragLeave(event: React.DragEvent<HTMLLabelElement>) {
    if (dropDisabled) return;
    event.preventDefault();
    setIsDragging(false);
  }

  function handleDrop(event: React.DragEvent<HTMLLabelElement>) {
    if (dropDisabled) return;
    event.preventDefault();
    setIsDragging(false);
    const dropped = event.dataTransfer.files?.[0] ?? null;
    if (dropped) {
      setMode("file");
      onFileChange(dropped);
    }
  }

  const ctaDisabled = isFileMode ? !canImport : !canCreateFromText;
  const ctaLabel = isFileMode ? "Upload and evaluate" : "Save and evaluate";
  const phaseLabel = phase === "reading"
    ? "Reading file..."
    : phase === "saving"
      ? "Saving source..."
      : phase === "extracting"
        ? "Extracting sections..."
        : phase === "analyzing"
          ? "Running review..."
          : isFileMode
            ? "Importing..."
            : "Saving...";
  const dropAsset = isImporting
    ? mascotAssets.loadingResumeScan
    : importFile
      ? mascotAssets.resumeBuddyCard
      : mascotAssets.resumeBuddyStack;
  const dropAssetLabel = isImporting
    ? "Scanning resume"
    : importFile
      ? "Resume selected"
      : "Resume upload";

  return (
    <div className={`mx-auto flex w-full flex-col items-center ${compact ? "max-w-3xl" : "max-w-xl"}`}>
      <p className="eyebrow">Upload resume</p>
      <h2 className={`${compact ? "mt-2 text-xl" : "mt-3 text-2xl"} font-semibold text-[var(--text-primary)]`}>
        Start with your resume
      </h2>
      <p className="mt-2 max-w-md text-sm leading-7 text-[var(--text-secondary)]">
        The resume agent will extract structured sections and run recruiter
        review on whichever input you provide.
      </p>

      <div
        role="tablist"
        aria-label="Resume input mode"
        className="mt-6 inline-flex rounded-full border border-[var(--border)] bg-white/55 p-1 text-xs font-medium"
      >
        <button
          type="button"
          role="tab"
          aria-selected={isFileMode}
          className={`rounded-full px-4 py-1.5 transition ${
            isFileMode
              ? "bg-[var(--accent)] text-white shadow-sm"
              : "text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
          }`}
          onClick={() => setMode("file")}
          disabled={isImporting}
        >
          Upload file
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={!isFileMode}
          className={`rounded-full px-4 py-1.5 transition ${
            !isFileMode
              ? "bg-[var(--accent)] text-white shadow-sm"
              : "text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
          }`}
          onClick={() => setMode("text")}
          disabled={isImporting}
        >
          Paste text
        </button>
      </div>

      {isFileMode ? (
        <>
          <label
            htmlFor={inputId}
            onDragOver={handleDragOver}
            onDragEnter={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            className={`${compact ? "mt-5 w-44 sm:w-52" : "mt-6 w-52 sm:w-64"} flex aspect-square cursor-pointer flex-col items-center justify-center rounded-full border-2 border-dashed text-center transition ${
              importFile
                ? "border-[var(--accent)] bg-[var(--accent-soft)]"
                : isDragging
                  ? "border-[var(--accent)] bg-[var(--accent-soft)] scale-[1.02]"
                  : "border-[var(--border-hover)] bg-white/45 hover:border-[var(--accent)] hover:bg-white/70"
            } ${dropDisabled ? "pointer-events-none opacity-55" : ""}`}
          >
            <SvgAsset
              src={dropAsset}
              label={dropAssetLabel}
              className={compact ? "h-20 w-20" : "h-24 w-24"}
            />
            <span className="mt-2 px-6 text-sm font-semibold text-[var(--text-primary)]">
              {isImporting
                ? phaseLabel.replace("...", "")
                : importFile
                ? "Resume file selected"
                : isDragging
                  ? "Drop to upload"
                  : "Choose or drop file"}
            </span>
            <span className="mt-1 text-[11px] text-[var(--text-tertiary)]">
              PDF, image, MD, TXT
            </span>
          </label>
          <input
            id={inputId}
            key={fileInputKey}
            className="sr-only"
            type="file"
            accept={acceptedResumeFileTypes}
            onChange={(event) => onFileChange(event.target.files?.[0] ?? null)}
            disabled={dropDisabled}
          />
          {importFile ? (
            <div className="mt-3 flex w-full max-w-sm items-center justify-between gap-3 rounded-md border border-[var(--border)] bg-white/60 px-3 py-2 text-xs">
              <div className="min-w-0">
                <p className="truncate font-medium text-[var(--text-primary)]" title={importFile.name}>
                  {importFile.name}
                </p>
                <p className="mt-0.5 text-[11px] text-[var(--text-tertiary)]">
                  {formatFileSize(importFile.size)} · {describeFileKind(importFile)}
                </p>
              </div>
              <button
                type="button"
                className="text-[11px] font-medium text-[var(--text-tertiary)] underline-offset-2 hover:text-[var(--text-primary)] hover:underline disabled:opacity-50"
                onClick={() => onFileChange(null)}
                disabled={isImporting}
              >
                Clear
              </button>
            </div>
          ) : null}
        </>
      ) : (
        <label className="mt-6 grid w-full max-w-sm gap-2 text-left">
          <span className="eyebrow">Resume text</span>
          <textarea
            className="input min-h-44 resize-y leading-6"
            value={sourceText}
            onChange={(event) => onSourceTextChange(event.target.value)}
            placeholder="Paste the resume content here when you do not want to upload a file."
            disabled={isImporting || !canPersist}
          />
          <span className="text-[11px] text-[var(--text-tertiary)]">
            {trimmedSourceLength >= 80
              ? `${trimmedSourceLength} characters ready`
              : `At least 80 characters required (${trimmedSourceLength} so far)`}
          </span>
        </label>
      )}

      <label className="mt-5 grid w-full max-w-sm gap-2 text-left">
        <span className="eyebrow">Optional title</span>
        <input
          className="input"
          value={title}
          onChange={(event) => onTitleChange(event.target.value)}
          placeholder="Spring 2026 SWE resume"
          maxLength={240}
          disabled={isImporting || !canPersist}
        />
      </label>

      <button
        type="button"
        className="btn btn-primary mt-5 w-full max-w-sm"
        disabled={ctaDisabled}
        onClick={() => void (isFileMode ? onImport() : onCreateFromText())}
      >
        {isImporting ? (
          <>
            <SvgAsset
              src={mascotAssets.loadingButtonBuddy}
              decorative
              className="h-6 w-12"
            />
            {phaseLabel}
          </>
        ) : (
          ctaLabel
        )}
      </button>
      {!canPersist ? (
        <p className="mt-3 text-sm text-[var(--text-tertiary)]">
          Live API is required to save resume data.
        </p>
      ) : null}
      {warning ? <p className="mt-3 text-sm text-[var(--orange)]">{warning}</p> : null}
      {notice ? <p className="mt-3 text-sm font-medium text-[var(--green)]">{notice}</p> : null}
      {error ? <p className="mt-3 text-sm font-medium text-[var(--red)]">{error}</p> : null}
    </div>
  );
}
