---
agent: agent
description: 'Standalone quality evaluation workflow that validates any workflow output against templates and requirements with conditional checks per workflow type'
tools: ['execute/createAndRunTask', 'read/readFile', 'azure-mcp/search', 'edit/createDirectory', 'edit/createFile', 'edit/editFiles', 'search', 'web', 'todo', 'propel-iq/*']
---

consult `.propel/prompts/evaluate-output.md` for the workflow steps.

---

*This evaluator provides 4-tier quality assessment with weighted scoring for workflow outputs.*
