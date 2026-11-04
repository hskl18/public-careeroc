export const defaultDemoEmailDomain = "demo.careeroc.example";
export const demoPersonaName = "Alex Rivera";
export const isDemoSnapshot = /^(1|true|yes)$/i.test(process.env.DEMO_SNAPSHOT?.trim() ?? "");
export const demoReadOnlyMessage = "This hosted demo is read-only. Clone the repo to try changes.";

export type DemoIdentity = {
  id: string;
  email: string;
  name: string;
};

// The hosted demo has one shared account: the one its snapshot was recorded for.
export const snapshotDemoIdentity: DemoIdentity = {
  id: "demo-alex-rivera",
  email: "alex.rivera@demo.careeroc.example",
  name: demoPersonaName,
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
