import "server-only";

import { redirect } from "next/navigation";
import { cache } from "react";
import { auth } from "@/auth";
import type { CareerocApiUser } from "./api-user";

// Next renders a page alongside its layout, so a redirect in the layout alone does not stop the page
// from loading data first. Every workspace page gets its identity here, with no fallback user.
export const requireUser = cache(async (): Promise<CareerocApiUser> => {
  const session = await auth();
  const email = session?.user?.email?.trim();
  if (!email) {
    redirect("/?next=/app");
  }

  return { email, name: session?.user?.name?.trim() || null };
});
