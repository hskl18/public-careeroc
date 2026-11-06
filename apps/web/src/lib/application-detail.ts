import type { components } from "./api-schema";

type Schemas = components["schemas"];

export type ApplicationAgentBrief = Schemas["ApplicationAgentBriefDto"];
export type AgentTraceSummary = Schemas["AgentTraceSummaryDto"];
export type AgentTraceFact = Schemas["ReviewAgentTraceFactDto"];
export type AgentTraceStep = Schemas["ReviewAgentTraceStepDto"];
export type AgentTrace = Schemas["ReviewAgentTraceDto"];
export type ApplicationDetailReminder = Schemas["ApplicationDetailReminderDto"];
export type ApplicationDetailActivity = Schemas["ApplicationDetailActivityDto"];
export type ApplicationContact = Schemas["ApplicationContactDto"];
export type ApplicationThreadMessage = Schemas["ApplicationThreadMessageDto"];
export type ApplicationThread = Schemas["ApplicationThreadDto"];
export type ApplicationFeedbackNote = Schemas["ApplicationFeedbackNoteDto"];
export type ApplicationDetail = Schemas["ApplicationDetailDto"];
