import type { PromptRung } from "./prompt-ladder.js";

export type PipelinePhaseDef = {
  id: string;
  title: string;
  rung: PromptRung | "compile_gate" | "pre_build" | "meat_loop";
  meatPass?: number;
  researchCategories: string[];
  webSearchQueries: string[];
  jsonPaths: string[];
  focus: string;
};
