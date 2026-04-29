import { StrictMode } from 'react'
import * as ReactDOM from 'react-dom'
import { createRoot } from 'react-dom/client'
import * as React from 'react'
import './index.css'
import App from './App.tsx'

// ─── Development-only accessibility auditing (US_100, AC-1) ───────────────────
// @axe-core/react runs WCAG 2.1 audits in the browser console every 1000 ms
// during development, flagging violations with severity, affected elements, and
// fix guidance.  The dynamic import is tree-shaken in production builds.
if (import.meta.env.DEV) {
  import('@axe-core/react').then(({ default: axe }) => {
    axe(React, ReactDOM, 1000)
  })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
