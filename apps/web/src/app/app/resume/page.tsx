import { requireUser } from "@/lib/require-user";
import { ResumeWorkspace } from "@/components/resume-workspace";
import { getResumeWorkspaceForUser } from "@/lib/resumes-server";

export default async function ResumePage() {
  const user = await requireUser();
  const { workspace, proxyPrefix } = await getResumeWorkspaceForUser(user);

  return <ResumeWorkspace initialWorkspace={workspace} proxyPrefix={proxyPrefix} />;
}
