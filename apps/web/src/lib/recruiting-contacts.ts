import type { components } from "./api-schema";

type Schemas = components["schemas"];

export type RecruitingContactRecentEmail = Schemas["RecruitingContactRecentEmailDto"];
export type RecruitingContact = Schemas["RecruitingContactDto"];
export type RecruitingContactListResponse = Schemas["RecruitingContactListResponse"];
export type UpdateRecruitingContactRequest = Schemas["UpdateRecruitingContactRequest"];
