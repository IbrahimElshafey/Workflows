# _Documents (Internal Project: _DOCS)

## 1. What is this?
A folder containing architecture reference sheets, technical specification guides, design pattern notes, versioning documentation, and roadmaps for the Workflows engine. Inside Visual Studio, this is managed via the shared project `_DOCS.shproj` to allow developers to search, browse, and edit documentation files directly inside the solution explorer.

This project does not compile into executable code or binary packages.

## 2. How to use?
Navigate the subdirectories using your IDE or file system to read about the design principles:
- **`Architecture/`**: Deep-dives into messaging, event dispatching, distributed timer scheduling, state hydration, OpenTelemetry, and poison message handling.
- **`Design_Patterns/`**: Guidelines for recursive matchers, sub-workflows, compensation logic (Saga pattern), and two-tier expression evaluation.
- **`Development/`**: Setup instruction guides, state management implementation notes, performance reviews, and reflection elimination benchmarks.
- **`HowTo/`**: Step-by-step developer tutorials, including workflow migration guides and index sitemaps.
- **`Roadmap/`**: Engine scaling specs, command specs, timer checking, source generator plans, Admin UI features, and versioning/out-of-process worker supervisor architecture.
