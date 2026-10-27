import { auth } from "@/auth";
import dynamic from "next/dynamic";
import { getApplicationDetailForUser } from "@/lib/application-detail-server";
import { getProfileForUser } from "@/lib/profile-server";

const ApplicationDetailWorkspace = dynamic(() =>
  import("@/components/application-detail-workspace").then(
    (module) => module.ApplicationDetailWorkspace,
  ),
);

type ApplicationDetailPageProps = {
  params: Promise<{
    applicationId: string;
  }>;
};

export default async function ApplicationDetailPage({
  params,
}: ApplicationDetailPageProps) {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const { applicationId } = await params;
  const [{ detail, source, proxyPrefix }, { profile }] = await Promise.all([
    getApplicationDetailForUser(user, applicationId),
    getProfileForUser(user),
  ]);

  return (
    <ApplicationDetailWorkspace
      detail={detail}
      source={source}
      proxyPrefix={proxyPrefix}
      currentUserEmail={user.email}
      currentUserName={user.name}
      timeZoneId={profile.timeZoneId}
    />
  );
}
