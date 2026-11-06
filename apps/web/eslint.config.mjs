import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    // The design system in UI.md: four type sizes, token colors, sentence case.
    files: ["src/**/*.tsx"],
    rules: {
      "no-restricted-syntax": [
        "error",
        ...["Literal[value", "TemplateElement[value.raw"].map((node) => ({
          selector: `${node}=/\\b(?:uppercase|tracking-|font-bold|text-\\[\\d|text-(?:lg|[2-9]xl)\\b|(?:bg|text)-(?:white|black)\\b|\\[rgba|rgba\\()/]`,
          message: "Use the design system in UI.md: four type sizes, token colors, sentence case.",
        })),
      ],
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    "playwright-report/**",
    "test-results/**",
  ]),
]);

export default eslintConfig;
