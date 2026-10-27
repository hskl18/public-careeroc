import { isDemoEmail } from "./demo";

export type AccessPolicy = {
  allowAllUsers: boolean;
  allowedEmails: string[];
  allowedDomains: string[];
  isProduction: boolean;
  // Set only when DEMO_MODE is on; demo identities are minted by this app, never by Google.
  demoEmailDomain: string | null;
};

export function isAccessAllowed(email: string | null | undefined, policy: AccessPolicy) {
  const normalizedEmail = email?.trim().toLowerCase() ?? "";
  if (!normalizedEmail) {
    return false;
  }

  if (policy.demoEmailDomain && isDemoEmail(normalizedEmail, policy.demoEmailDomain)) {
    return true;
  }

  if (policy.allowAllUsers) {
    return true;
  }

  if (!policy.isProduction && policy.allowedEmails.length === 0 && policy.allowedDomains.length === 0) {
    return true;
  }

  const domain = normalizedEmail.split("@").at(-1) ?? "";
  return policy.allowedEmails.includes(normalizedEmail) || policy.allowedDomains.includes(domain);
}
