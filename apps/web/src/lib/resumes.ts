import type { components } from "./api-schema";

type Schemas = components["schemas"];

export type ResumeWorkspace = Schemas["ResumeWorkspaceDto"];
export type ResumeDocument = Schemas["ResumeDocumentDto"];
export type ResumeVersion = Schemas["ResumeVersionDto"];
export type ResumeStructuredData = Schemas["ResumeStructuredDataDto"];
export type ResumeContact = Schemas["ResumeContactDto"];
export type ResumeLink = Schemas["ResumeLinkDto"];
export type ResumeExperience = Schemas["ResumeExperienceDto"];
export type ResumeEducation = Schemas["ResumeEducationDto"];
export type ResumeSkillGroups = Schemas["ResumeSkillGroupsDto"];
export type ResumeProject = Schemas["ResumeProjectDto"];
export type ResumeEvaluation = Schemas["ResumeEvaluationDto"];
export type ResumeFit = Schemas["ResumeFitDto"];
export type ResumeExtractionMetadata = Schemas["ResumeExtractionMetadataDto"];
export type ResumeSectionReview = Schemas["ResumeSectionReviewDto"];
export type ResumeEvaluationEvidence = Schemas["ResumeEvaluationEvidenceDto"];
export type ResumeReadiness = Schemas["ResumeReadinessDto"];
export type CreateResumeRequest = Schemas["CreateResumeRequest"];

export const fallbackResumeWorkspace: ResumeWorkspace = {
  documents: [],
  readiness: {
    hasResume: false,
    summary: "Resume intelligence is unavailable until the live API is connected.",
    nextActions: ["Reconnect the API before creating resume evaluations."],
  },
};
