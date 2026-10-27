export const defaultDemoEmailDomain = "demo.careeroc.example";
export const demoPersonaName = "Alex Rivera";

export type DemoIdentity = {
  id: string;
  email: string;
  name: string;
};

export function isDemoEmail(email: string | null | undefined, domain: string) {
  const normalized = email?.trim().toLowerCase() ?? "";
  return normalized.endsWith(`@${domain.trim().toLowerCase()}`) && normalized.indexOf("@") > 0;
}

// Each visitor gets a fresh identity, so demo workspaces never mix data between people.
export function createDemoIdentity(domain: string, randomId: string = crypto.randomUUID()): DemoIdentity {
  const token = randomId.replaceAll("-", "").slice(0, 16).toLowerCase();
  return {
    id: `demo-${token}`,
    email: `visitor-${token}@${domain.trim().toLowerCase()}`,
    name: demoPersonaName,
  };
}
