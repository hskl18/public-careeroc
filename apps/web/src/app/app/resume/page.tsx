import { auth } from "@/auth";
import { ResumeWorkspace } from "@/components/resume-workspace";
import { getResumeWorkspaceForUser } from "@/lib/resumes-server";

type ResumePageProps = {
  searchParams?: Promise<{
    applicationId?: string;
  }>;
};

export default async function ResumePage({ searchParams }: ResumePageProps) {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const params = await searchParams;
  const { workspace, source, proxyPrefix } = await getResumeWorkspaceForUser(user);

  return (
    <ResumeWorkspace
      initialWorkspace={workspace}
      source={source}
      proxyPrefix={proxyPrefix}
      currentUserEmail={user.email}
      currentUserName={user.name}
      applicationId={params?.applicationId ?? null}
    />
  );
}
