import { readdir, stat } from "node:fs/promises";
import path from "node:path";

const defaultBudgetBytes = 1_400_000;
const budgetBytes = Number(process.env.WEB_BUNDLE_BUDGET_BYTES ?? defaultBudgetBytes);
const chunksDir = path.join(process.cwd(), ".next", "static", "chunks");

async function collectJavaScriptBytes(directory) {
  let total = 0;
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      total += await collectJavaScriptBytes(absolutePath);
      continue;
    }

    if (entry.isFile() && (entry.name.endsWith(".js") || entry.name.endsWith(".mjs"))) {
      total += (await stat(absolutePath)).size;
    }
  }

  return total;
}

try {
  const totalBytes = await collectJavaScriptBytes(chunksDir);
  const totalKb = Math.round(totalBytes / 1024);
  const budgetKb = Math.round(budgetBytes / 1024);

  if (totalBytes > budgetBytes) {
    console.error(
      `Web bundle budget exceeded: ${totalKb} KB generated, budget is ${budgetKb} KB.`,
    );
    process.exit(1);
  }

  console.log(`Web bundle budget ok: ${totalKb} KB generated, budget is ${budgetKb} KB.`);
} catch (error) {
  console.error(
    "Unable to read .next/static/chunks. Run `pnpm --dir apps/web build` first.",
  );
  if (error instanceof Error) {
    console.error(error.message);
  }
  process.exit(1);
}
