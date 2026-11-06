import type { components } from "./api-schema";

type Schemas = components["schemas"];

export type ReviewApplication = Schemas["ReviewApplicationDto"];
export type ReviewEmail = Schemas["ReviewEmailDto"];
export type ReviewThreadMessage = Schemas["ReviewThreadMessageDto"];
export type ReviewAgentTraceFact = Schemas["ReviewAgentTraceFactDto"];
export type ReviewAgentTraceStep = Schemas["ReviewAgentTraceStepDto"];
export type ReviewAgentTrace = Schemas["ReviewAgentTraceDto"];
export type AgentTraceSummary = Schemas["AgentTraceSummaryDto"];
export type ManualReviewResponse = Schemas["ManualReviewResponse"];
