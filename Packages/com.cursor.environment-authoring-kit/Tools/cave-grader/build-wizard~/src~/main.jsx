import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import WizardShell from "./WizardShell.jsx";
import "./styles.css";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <WizardShell />
  </StrictMode>
);
