import type { UserMemoryEntry } from "./profile";

export type ResumeWorkspace = {
  documents: ResumeDocument[];
  readiness: ResumeReadiness;
};

export type ResumeDocument = {
  id: string;
  title: string;
  sourceKind: string;
  sourceFileName: string | null;
  rawSourceCharacterCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  latestVersion: ResumeVersion | null;
  latestEvaluation: ResumeEvaluation | null;
  memoryProposals?: UserMemoryEntry[] | null;
};

export type ResumeVersion = {
  id: string;
  resumeDocumentId: string;
  summary: string;
  skills: string[];
  projectHighlights: string[];
  structuredData?: ResumeStructuredData | null;
  extractionMetadata: ResumeExtractionMetadata;
  experienceCount: number;
  projectCount: number;
  createdAtUtc: string;
  corrections: ResumeCorrection[];
};

export type ResumeStructuredData = {
  contact: ResumeContact;
  experience: ResumeExperience[];
  education: ResumeEducation[];
  skills: ResumeSkillGroups;
  projects: ResumeProject[];
};

export type ResumeContact = {
  name: string;
  email: string;
  phone: string;
  location: string;
  links: ResumeLink[];
};

export type ResumeLink = {
  label: string;
  url: string;
};

export type ResumeExperience = {
  company: string;
  title: string;
  location: string;
  dateRange: string;
  bullets: string[];
};

export type ResumeEducation = {
  school: string;
  degree: string;
  field: string;
  dateRange: string;
  gpa: string;
  notes: string[];
};

export type ResumeSkillGroups = {
  technical: string[];
  languages: string[];
  other: string[];
};

export type ResumeProject = {
  name: string;
  description: string;
  tech: string[];
  links: string[];
  bullets: string[];
};

export type ResumeCorrection = {
  id: string;
  resumeVersionId: string;
  fieldPath: string;
  previousValue: string | null;
  correctedValue: string;
  note: string | null;
  createdAtUtc: string;
};

export type ResumeEvaluation = {
  id: string;
  resumeVersionId: string;
  score: number;
  headline: string;
  verdict: string;
  strengths: string[];
  gaps: string[];
  nextActions: string[];
  sectionReviews: ResumeSectionReview[];
  evidence: ResumeEvaluationEvidence;
  modelRun: ResumeModelRun | null;
  createdAtUtc: string;
};

export type ResumeFit = {
  jobApplicationId: string;
  resumeVersionId: string | null;
  matchScore: number;
  strengths: string[];
  gaps: string[];
  rationale: string | null;
};

export type ResumeSuggestion = {
  id: string;
  kind: string | null;
  targetSection: string | null;
  originalText: string | null;
  suggestedText: string | null;
  rationale: string | null;
  evidenceLinks: string[];
};

export type ResumeExtractionMetadata = {
  sourceKind: string;
  sourceFileName: string | null;
  rawSourceCharacterCount: number;
  extractionMethod: string;
  extractedAtUtc: string;
  contactFieldCount: number;
  experienceCount: number;
  educationCount: number;
  projectCount: number;
  skillCount: number;
  promptVersion: string | null;
};

export type ResumeSectionReview = {
  section: string;
  score: number;
  strengths: string[];
  weaknesses: string[];
  suggestions: string[];
};

export type ResumeModelRun = {
  runId: string;
  purpose: string;
  provider: string | null;
  model: string | null;
  outcome: string;
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  estimatedCostUsd: number | null;
  latencyMs: number | null;
  occurredAtUtc: string | null;
  errorType: string | null;
  statusCode: number | null;
};

export type ResumeEvaluationEvidence = {
  skillCount: number;
  experienceCount: number;
  projectCount: number;
  hasEducation: boolean;
  hasContact: boolean;
  hasLinks: boolean;
  applicationCount: number;
  interviewOrOfferCount: number;
  targetRole: string | null;
};

export type ResumeReadiness = {
  hasResume: boolean;
  summary: string;
  nextActions: string[];
};

export type CreateResumeRequest = {
  title: string | null;
  sourceKind: string | null;
  sourceText: string;
  sourceFileName?: string | null;
};

export type ImportResumeFileRequest = {
  title: string | null;
  fileName: string;
  contentType: string;
  base64Content: string;
};

export type CorrectResumeFieldRequest = {
  fieldPath: string;
  previousValue: string | null;
  correctedValue: string;
  note: string | null;
};

export type PatchResumeStructuredDataRequest = {
  operations: ResumeStructuredDataPatchOperation[];
  note: string | null;
};

export type ResumeStructuredDataPatchOperation = {
  op: "add" | "replace" | "remove";
  path: string;
  value?: unknown;
};

export const fallbackResumeWorkspace: ResumeWorkspace = {
  documents: [],
  readiness: {
    hasResume: false,
    summary: "Resume intelligence is unavailable until the live API is connected.",
    nextActions: ["Reconnect the API before creating resume evaluations."],
  },
};
