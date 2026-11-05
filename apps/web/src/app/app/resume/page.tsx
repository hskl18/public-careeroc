import { auth } from "@/auth";
import { ResumeWorkspace } from "@/components/resume-workspace";
import { getResumeWorkspaceForUser } from "@/lib/resumes-server";

export default async function ResumePage() {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const { workspace, proxyPrefix } = await getResumeWorkspaceForUser(user);

  return <ResumeWorkspace initialWorkspace={workspace} proxyPrefix={proxyPrefix} />;
}
