import type { components } from "@/lib/api-schema";

type RecruitingContactDto = components["schemas"]["RecruitingContactDto"];
type RecruitingContactRecentEmailDto =
  components["schemas"]["RecruitingContactRecentEmailDto"];
export type UpdateRecruitingContactRequest =
  components["schemas"]["UpdateRecruitingContactRequest"];

export type RecruitingContactRecentEmail = {
  id: string;
  jobApplicationId: string | null;
  company: string | null;
  role: string | null;
  subject: string;
  sender: string;
  receivedAtUtc: string;
};

export type RecruitingContact = {
  id: string;
  email: string;
  displayName: string | null;
  companyHint: string | null;
  roleHint: string | null;
  contactType: string | null;
  confidence: number | null;
  messageCount: number;
  firstSeenAtUtc: string;
  lastSeenAtUtc: string;
  recentEmails: RecruitingContactRecentEmail[];
};

export type RecruitingContactListResponse = {
  contacts: RecruitingContact[];
};

export function mapRecruitingContactListResponse(
  response: components["schemas"]["RecruitingContactListResponse"],
): RecruitingContactListResponse {
  return {
    contacts: (response.contacts ?? [])
      .map(mapRecruitingContact)
      .filter((contact): contact is RecruitingContact => contact !== null),
  };
}

export function mapRecruitingContact(
  contact: RecruitingContactDto,
): RecruitingContact | null {
  if (!contact.id || !contact.email || !contact.firstSeenAtUtc || !contact.lastSeenAtUtc) {
    return null;
  }

  return {
    id: contact.id,
    email: contact.email,
    displayName: contact.displayName ?? null,
    companyHint: contact.companyHint ?? null,
    roleHint: contact.roleHint ?? null,
    contactType: contact.contactType ?? null,
    confidence: contact.confidence ?? null,
    messageCount: contact.messageCount ?? 0,
    firstSeenAtUtc: contact.firstSeenAtUtc,
    lastSeenAtUtc: contact.lastSeenAtUtc,
    recentEmails: (contact.recentEmails ?? [])
      .map(mapRecentEmail)
      .filter((email): email is RecruitingContactRecentEmail => email !== null),
  };
}

function mapRecentEmail(
  email: RecruitingContactRecentEmailDto,
): RecruitingContactRecentEmail | null {
  if (!email.id || !email.subject || !email.sender || !email.receivedAtUtc) {
    return null;
  }

  return {
    id: email.id,
    jobApplicationId: email.jobApplicationId ?? null,
    company: email.company ?? null,
    role: email.role ?? null,
    subject: email.subject,
    sender: email.sender,
    receivedAtUtc: email.receivedAtUtc,
  };
}
