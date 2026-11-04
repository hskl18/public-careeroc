"use client";

import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { useRouter } from "next/navigation";
import {
  ProfileIcon,
  defaultProfileIcon,
  profileIconOptions,
} from "@/components/profile-icon";
import type {
  UpdateUserProfileRequest,
  UserProfile,
} from "@/lib/profile";

type ProfileSettingsPanelProps = {
  initialProfile: UserProfile;
  loadedFromApi: boolean;
  proxyPrefix: string | null;
  email: string;
  displayName: string;
};

const timeZoneOptions = [
  { value: "America/Los_Angeles", label: "Pacific Time" },
  { value: "America/Denver", label: "Mountain Time" },
  { value: "America/Chicago", label: "Central Time" },
  { value: "America/New_York", label: "Eastern Time" },
  { value: "Etc/UTC", label: "UTC" },
  { value: "Europe/London", label: "London" },
  { value: "Europe/Berlin", label: "Berlin" },
  { value: "Asia/Shanghai", label: "Shanghai" },
  { value: "Asia/Tokyo", label: "Tokyo" },
  { value: "Asia/Seoul", label: "Seoul" },
  { value: "Asia/Singapore", label: "Singapore" },
  { value: "Asia/Kolkata", label: "India" },
] as const;

async function readErrorMessage(response: Response) {
  if (response.status === 404) {
    return "Profile settings are not available right now. Try again in a moment.";
  }

  if (response.status >= 500) {
    return "Profile API failed in the backend. Restart the API and make sure the latest migration has been applied.";
  }

  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

function normalizeNullable(value: string) {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function getTimeZoneLabel(value: string) {
  return timeZoneOptions.find((option) => option.value === value)?.label ?? value;
}

export function ProfileSettingsPanel({
  initialProfile,
  loadedFromApi,
  proxyPrefix,
  email,
  displayName,
}: ProfileSettingsPanelProps) {
  const router = useRouter();
  const [profile, setProfile] = useState(initialProfile);
  const [name, setName] = useState(initialProfile.name);
  const [timeZoneId, setTimeZoneId] = useState(initialProfile.timeZoneId);
  const [profileIcon, setProfileIcon] = useState(
    initialProfile.profileIcon ?? defaultProfileIcon,
  );
  const [location, setLocation] = useState(initialProfile.location ?? "");
  const [targetRole, setTargetRole] = useState(initialProfile.targetRole ?? "");
  const [isSaving, setIsSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const hasChanges = useMemo(
    () =>
      name.trim() !== profile.name ||
      timeZoneId !== profile.timeZoneId ||
      profileIcon !== (profile.profileIcon ?? defaultProfileIcon) ||
      normalizeNullable(location) !== profile.location ||
      normalizeNullable(targetRole) !== profile.targetRole,
    [location, name, profile, profileIcon, targetRole, timeZoneId],
  );
  const canSave = Boolean(proxyPrefix) && hasChanges && !isSaving;
  const saveStatus = isSaving
    ? "Saving..."
    : error
      ? "Couldn't save. Try again."
      : hasChanges
        ? "Unsaved changes"
        : notice
          ? "Saved just now"
          : "All changes saved";
  const saveStatusClass = error
    ? "text-[var(--red)]"
    : hasChanges
      ? "text-[var(--accent)]"
      : "text-[var(--text-tertiary)]";
  useEffect(() => {
    queueMicrotask(() => {
      setProfile(initialProfile);
      setName(initialProfile.name);
      setTimeZoneId(initialProfile.timeZoneId);
      setProfileIcon(initialProfile.profileIcon ?? defaultProfileIcon);
      setLocation(initialProfile.location ?? "");
      setTargetRole(initialProfile.targetRole ?? "");
    });
  }, [initialProfile]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave || !proxyPrefix) return;

    const request: UpdateUserProfileRequest = {
      name: name.trim(),
      timeZoneId,
      profileIcon,
      location: normalizeNullable(location),
      targetRole: normalizeNullable(targetRole),
    };

    setIsSaving(true);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/profile`, {
        method: "PATCH",
        headers: {
          "content-type": "application/json",
        },
        body: JSON.stringify(request),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const nextProfile = (await response.json()) as UserProfile;
      const normalizedProfile = {
        ...nextProfile,
        profileIcon: nextProfile.profileIcon ?? defaultProfileIcon,
      };
      setProfile(normalizedProfile);
      setName(nextProfile.name);
      setTimeZoneId(nextProfile.timeZoneId);
      setProfileIcon(normalizedProfile.profileIcon);
      setLocation(nextProfile.location ?? "");
      setTargetRole(nextProfile.targetRole ?? "");
      setNotice("Profile saved.");
      router.refresh();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Profile save failed.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <form
      className="card flex h-full min-h-0 flex-col overflow-hidden p-0"
      onSubmit={handleSubmit}
    >
      <div className="shrink-0 px-4 pt-4 sm:px-5 sm:pt-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 items-center gap-3">
            <ProfileIcon
              icon={profileIcon}
              label={`${displayName} profile icon`}
              className="h-12 w-12 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)]"
            />
            <div className="min-w-0">
              <p className="eyebrow">Candidate</p>
              <h1 className="mt-2 truncate text-xl font-semibold text-[var(--text-primary)]">
                {displayName}
              </h1>
              <p className="mt-1 break-all text-sm text-[var(--text-secondary)]">
                {email}
              </p>
            </div>
          </div>
          <div className="flex flex-col items-start gap-2 sm:items-end">
            <p className={`text-xs font-medium ${saveStatusClass}`}>
              {saveStatus}
            </p>
            <button type="submit" className="btn btn-primary btn-sm" disabled={!canSave}>
              {isSaving ? <><span className="spinner" /> Saving...</> : "Save changes"}
            </button>
          </div>
        </div>
      </div>

      <div className="shrink-0 px-4 py-4 sm:px-5">
        <div className="border-t border-[var(--border)] pt-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="eyebrow">Profile details</p>
              <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
                Candidate profile
              </h2>
              <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
                Used to tailor resume feedback, job recommendations, and follow-up strategy.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
                {getTimeZoneLabel(profile.timeZoneId)}
              </span>
            </div>
          </div>
        </div>
      </div>

      <div className="px-4 pt-4 pb-4 sm:px-5 sm:pb-5">
        <section>
          <div className="grid gap-4 sm:grid-cols-2">
          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Name</span>
            <input
              className="min-h-11 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 text-sm font-medium text-[var(--text-primary)] outline-none transition focus:border-[var(--accent)]"
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={200}
              disabled={isSaving}
            />
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Time zone</span>
            <select
              className="min-h-11 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 text-sm font-medium text-[var(--text-primary)] outline-none transition focus:border-[var(--accent)]"
              value={timeZoneId}
              onChange={(event) => setTimeZoneId(event.target.value)}
              disabled={isSaving}
            >
              {timeZoneOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Location</span>
            <input
              className="min-h-11 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 text-sm font-medium text-[var(--text-primary)] outline-none transition focus:border-[var(--accent)]"
              value={location}
              onChange={(event) => setLocation(event.target.value)}
              maxLength={160}
              placeholder="Portland, OR"
              disabled={isSaving}
            />
          </label>

          <label className="grid gap-2 sm:col-span-1">
            <span className="eyebrow">Target role</span>
            <input
              className="min-h-11 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 text-sm font-medium text-[var(--text-primary)] outline-none transition focus:border-[var(--accent)]"
              value={targetRole}
              onChange={(event) => setTargetRole(event.target.value)}
              maxLength={200}
              placeholder="Software engineering intern"
              disabled={isSaving}
            />
          </label>
        </div>
      </section>

      <section className="mt-5 border-t border-[var(--border)] pt-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="eyebrow">Profile icon</p>
            <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
              Choose a profile icon
            </h2>
          </div>
        </div>
        <div
          className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-5"
          role="radiogroup"
          aria-label="Profile icon"
        >
          {profileIconOptions.map((option) => {
            const selected = profileIcon === option.id;

            return (
              <button
                key={option.id}
                type="button"
                role="radio"
                aria-checked={selected}
                className={`relative flex min-h-20 items-center gap-2 rounded-md border px-2 py-2 text-left transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)] ${
                  selected
                    ? "border-[var(--accent)] bg-[var(--accent-soft)] shadow-[0_0_0_1px_var(--accent)]"
                    : "border-[var(--border)] bg-[var(--surface-elevated)]/60 hover:border-[var(--border-hover)] hover:bg-[var(--surface-elevated)]"
                }`}
                disabled={isSaving}
                onClick={() => setProfileIcon(option.id)}
              >
                {selected ? (
                  <span className="absolute right-2 top-2 flex h-5 w-5 items-center justify-center rounded-full bg-[var(--accent)] text-[10px] font-bold text-white">
                    ✓
                  </span>
                ) : null}
                <ProfileIcon icon={option.id} decorative className="h-9 w-9 shrink-0" />
                <span className="min-w-0">
                  <span className="block text-xs font-semibold leading-tight text-[var(--text-primary)]">
                    {option.label}
                  </span>
                  {selected ? (
                    <span className="mt-1 block text-[10px] font-semibold uppercase tracking-[0.14em] text-[var(--accent)]">
                      Selected
                    </span>
                  ) : null}
                </span>
              </button>
            );
          })}
        </div>
      </section>

      <div className="mt-4 flex flex-col gap-2">
        {notice ? (
          <p className="text-sm font-medium text-[var(--green)]">{notice}</p>
        ) : null}
        {error ? (
          <p className="text-sm font-medium text-[var(--red)]">{error}</p>
        ) : null}
        {!proxyPrefix ? (
          <p className="text-sm text-[var(--text-tertiary)]">
            Live sync is required to save profile settings.
          </p>
        ) : !loadedFromApi && !error && !notice ? (
          <p className="text-sm text-[var(--text-tertiary)]">
            Profile data is using fallback values right now. Save will work after the backend exposes the latest profile API.
          </p>
        ) : null}
      </div>
      </div>
    </form>
  );
}
