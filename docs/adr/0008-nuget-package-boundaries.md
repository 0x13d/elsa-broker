---
status: accepted
date: 2026-06-08
decision-makers: dotnet-architect, project-manager
consulted: db-architect, test-lead, docs-web
informed: security
---

# NuGet package boundaries for ElsaBroker

## Context and Problem Statement

ElsaBroker is evolving from a single-deployment solution into a reusable, opinionated Elsa deployment
strategy. As we approach 1.0, we must define which assemblies ship as NuGet packages, which ship as
Docker images, and where the public API boundary sits for SemVer purposes.

Today the solution has seven projects, but only a subset are meaningful as library packages. The key
tension is between **consumer ergonomics** (fewer packages to install) and **dependency isolation**
(a custom `ILogShipper` author should not need to pull in EF Core 9 and MassTransit). Getting this
wrong locks us into a package graph that either forces unnecessary transitive dependencies on consumers
or fragments the API into too many packages for a pre-1.0 project.

The decision also defines what "public API" means for SemVer 1.0.0: any type in a published NuGet
package becomes a compatibility commitment.

## Decision Drivers

* **Consumer ergonomics** -- adopters should `dotnet add package` a small number of packages and get a
  working setup, not chase a dependency tree.
* **Contracts stability** -- `ISubmitRequest` and `RequestStatus` are the wire contract shared between
  producers and consumers. They must be versionable independently with strict SemVer and must never
  carry transitive dependencies.
* **Extensibility without weight** -- consumers implementing `ILogShipper` or `IRequestHandler` need a
  reference target that does not drag in EF Core, MassTransit, or SQL Server packages.
* **EF migration coupling** -- `BrokerDbContext` and its migrations must ship together; consumers who
  run `dotnet ef database update` need the Data assembly, but extensibility authors should not.
* **Host apps are not libraries** -- Queue, Processor, WorkflowServer, and CertTools are runnable
  services/tools. They ship as Docker images or `dotnet new` templates, never as NuGet library packages.
* **Avoid premature splitting** -- the project is pre-1.0 with a small adopter base. Creating ten
  packages invites diamond dependency problems and increases the release ceremony per version bump.
  We can split later; merging is nearly impossible.
* **The path to 1.0.0** -- this ADR's recommendation defines the public surface area for SemVer. Every
  type in a published package becomes a backward-compatibility commitment.

## Considered Options

* Option A: Two packages (conservative)
* Option B: Three packages (balanced)
* Option C: Five packages (modular)

## Decision Outcome

Chosen option: **Option B (Three packages)**, because it satisfies the two critical isolation
requirements -- dependency-free wire contracts and lightweight extensibility -- without the release
overhead and premature granularity of five packages. It is the smallest packaging that keeps the
extensibility seam (`ILogShipper`, `IRequestHandler`) free of infrastructure weight.

### Consequences

* Good, because custom shipper/handler authors reference only `ElsaBroker.Abstractions` (zero heavy
  NuGet dependencies) and can compile, test, and ship without EF Core or MassTransit on the dependency
  graph.
* Good, because `ElsaBroker.Contracts` remains dependency-free and independently versionable, so
  message producers and consumers can pin different minor versions without conflict.
* Good, because `ElsaBroker` (the meta-package) gives service authors a single `dotnet add package`
  that brings in everything needed to wire up a host -- DbContext, migrations, registry, logging
  implementations, consumer, DI extensions.
* Good, because three packages is a manageable release surface for a pre-1.0 project: one `dotnet pack`
  produces three `.nupkg` files, each with a clear owner and change cadence.
* Bad, because creating `ElsaBroker.Abstractions` requires extracting `ILogShipper`, `BrokerLogEvent`,
  `IRequestHandler`, and `RequestResult` out of their current homes (`ElsaBroker.Data.Logging` and
  `ElsaBroker.Processor.Handlers`) into a new project. This is a one-time restructuring cost.
* Bad, because three packages still means three version numbers to coordinate. Lock-step versioning
  (all three always share the same version) is recommended pre-1.0 to avoid confusion.
* Neutral, because `ElsaBroker.Data` and the shipper implementations remain internal to the
  `ElsaBroker` meta-package. If demand emerges for finer-grained Data-vs-Logging splits, we can carve
  them out post-1.0 as a non-breaking additive change.

### Confirmation

The following checks validate that the package boundaries are correctly implemented:

1. **`dotnet pack` produces exactly three `.nupkg` files:**
   `ElsaBroker.Contracts`, `ElsaBroker.Abstractions`, `ElsaBroker`.
2. **Dependency graph test:** `ElsaBroker.Contracts.nupkg` has zero NuGet dependencies.
   `ElsaBroker.Abstractions.nupkg` depends only on `ElsaBroker.Contracts` (no EF, no MassTransit).
   `ElsaBroker.nupkg` depends on `ElsaBroker.Abstractions` (and transitively `Contracts`) plus EF Core
   and MassTransit.
3. **Reference compilation test:** a test project that references only `ElsaBroker.Abstractions` can
   implement `ILogShipper` and `IRequestHandler` and compile without any other package.
4. **Host projects (Queue, Processor, WorkflowServer, CertTools) have `<IsPackable>false</IsPackable>`**
   and do not appear in `dotnet pack` output.
5. **ArchUnit-style test (future):** no type in `ElsaBroker.Contracts` or `ElsaBroker.Abstractions`
   references a namespace from `Microsoft.EntityFrameworkCore` or `MassTransit`.

## Pros and Cons of the Options

### Option A: Two packages (conservative)

Ship `ElsaBroker.Contracts` (wire contract, no deps) and `ElsaBroker` (everything else -- Data,
registry, logging interfaces and implementations, consumer, DI helpers). Host apps ship as Docker images.

```
ElsaBroker.Contracts  (no deps)
     ^
     |
ElsaBroker            (EF Core + MassTransit + SqlServer)
```

* Good, because it is the simplest possible package graph: two packages, one dependency edge.
* Good, because it minimizes release ceremony and eliminates any risk of version-matrix confusion.
* Bad, because **a consumer who only wants to implement `ILogShipper` must pull in EF Core 9,
  MassTransit 8.3, and `Microsoft.EntityFrameworkCore.SqlServer`** -- approximately 40+ transitive
  NuGet packages -- just to reference a two-method interface.
* Bad, because `IRequestHandler` (currently in `ElsaBroker.Processor`) cannot be offered to external
  authors without either (a) moving it into the heavy `ElsaBroker` package or (b) leaving it
  undiscoverable in a host-only assembly.
* Bad, because it makes the public API surface of `ElsaBroker` very large (entities, DbContext,
  registry internals, all shipper implementations), increasing the SemVer commitment surface
  unnecessarily.

### Option B: Three packages (balanced) -- RECOMMENDED

Ship `ElsaBroker.Contracts` (wire contract), `ElsaBroker.Abstractions` (extensibility interfaces), and
`ElsaBroker` (infrastructure meta-package). Host apps ship as Docker images.

```
ElsaBroker.Contracts      (no deps)
     ^
     |
ElsaBroker.Abstractions   (no heavy deps -- depends only on Contracts)
     ^
     |
ElsaBroker                (EF Core + MassTransit + SqlServer)
```

* Good, because the extensibility seam is cleanly separated: `ILogShipper`, `BrokerLogEvent`,
  `IRequestHandler`, and `RequestResult` live in a package with no infrastructure dependencies.
* Good, because three packages is few enough to version in lock-step pre-1.0 without overhead.
* Good, because it maps naturally to consumer roles: message **producers** reference `Contracts`,
  extensibility **authors** reference `Abstractions`, service **hosts** reference `ElsaBroker`.
* Neutral, because it requires a one-time extraction of four types into a new
  `ElsaBroker.Abstractions` project, plus updating `using` directives in Data and Processor. This is
  mechanical and low-risk.
* Bad, because three packages is one more than the absolute minimum.

### Option C: Five packages (modular)

Ship `ElsaBroker.Contracts`, `ElsaBroker.Abstractions`, `ElsaBroker.Data`, `ElsaBroker.Logging`, and
`ElsaBroker` (meta-package/DI). Host apps ship as Docker images.

```
ElsaBroker.Contracts      (no deps)
     ^
     |
ElsaBroker.Abstractions   (no heavy deps)
     ^
     +------------------+
     |                  |
ElsaBroker.Data       ElsaBroker.Logging
  (EF Core)             (Console, HTTP shippers)
     ^                  ^
     +--------+---------+
              |
         ElsaBroker       (DI extensions, ties it all together)
```

* Good, because consumers can reference `ElsaBroker.Data` for migrations without pulling in logging
  implementations, or reference `ElsaBroker.Logging` for built-in shippers without EF Core.
* Good, because each package has a narrow, well-defined responsibility.
* Bad, because **five packages for a pre-1.0 project with a small user base is premature**. The
  version matrix grows combinatorially; every release requires publishing five coordinated packages.
* Bad, because the `ElsaBroker.Logging` package contains only three small classes
  (`ConsoleLogShipper`, `HttpJsonLogShipper`, `CompositeLogShipper`) -- not enough surface to justify
  a standalone package.
* Bad, because splitting Data from Logging creates a diamond dependency if a consumer needs both
  (which virtually all hosts do), adding complexity without practical benefit.
* Neutral, because this granularity can be reached later by splitting `ElsaBroker` post-1.0 if
  real demand emerges. Splitting a package is additive; merging is not.

## Package Dependency Diagram

```
+---------------------------+
|  ElsaBroker.Contracts     |  NuGet deps: (none)
|  net9.0 class library     |
|                           |
|  ISubmitRequest           |
|  RequestStatus            |
+-------------+-------------+
              ^
              |  PackageReference
              |
+-------------+-------------+
|  ElsaBroker.Abstractions  |  NuGet deps: (none, only project-ref to Contracts)
|  net9.0 class library     |
|                           |
|  ILogShipper              |
|  BrokerLogEvent           |
|  IRequestHandler          |
|  RequestResult            |
+-------------+-------------+
              ^
              |  PackageReference
              |
+-------------+------------------+
|  ElsaBroker                    |  NuGet deps: EF Core 9, MassTransit 8.3,
|  net9.0 class library          |  MassTransit.EntityFrameworkCore,
|                                |  MassTransit.SqlTransport.SqlServer,
|  BrokerDbContext + Migrations  |  Microsoft.EntityFrameworkCore.SqlServer
|  RequestRecord                 |
|  RequestTypeDefinition         |
|  RequestTypeRegistry           |
|  RegistryLoader                |
|  WorkflowRegistry              |
|  WorkflowFolderScanner         |
|  ConsoleLogShipper             |
|  HttpJsonLogShipper            |
|  CompositeLogShipper           |
|  NullLogShipper                |
|  LogShipperExtensions          |
|  (future: DI extension methods)|
+--------------------------------+

NOT packaged (Docker images / tools only):
  ElsaBroker.Queue           (ASP.NET Core host -- IsPackable=false)
  ElsaBroker.Processor       (Worker host -- IsPackable=false)
  ElsaBroker.WorkflowServer  (Elsa host -- IsPackable=false)
  ElsaBroker.CertTools       (CLI tool -- IsPackable=false)
  ElsaBroker.Tests           (test project -- IsPackable=false)
```

## Public API Surface Table

| Type | Current Location | Target NuGet Package | Notes |
|------|-----------------|---------------------|-------|
| `ISubmitRequest` | `ElsaBroker.Contracts` | **ElsaBroker.Contracts** | Wire contract. Additive-only. |
| `RequestStatus` | `ElsaBroker.Contracts` | **ElsaBroker.Contracts** | Status constants. Additive-only. |
| `ILogShipper` | `ElsaBroker.Data.Logging` | **ElsaBroker.Abstractions** | Must move. Extensibility interface. |
| `BrokerLogEvent` | `ElsaBroker.Data.Logging` | **ElsaBroker.Abstractions** | Must move. DTO consumed by shippers. |
| `IRequestHandler` | `ElsaBroker.Processor.Handlers` | **ElsaBroker.Abstractions** | Must move. Extensibility interface. |
| `RequestResult` | `ElsaBroker.Processor.Handlers` | **ElsaBroker.Abstractions** | Must move. Return type for handlers. |
| `BrokerDbContext` | `ElsaBroker.Data` | **ElsaBroker** | EF context + inbox/outbox tables. |
| `RequestRecord` | `ElsaBroker.Data.Entities` | **ElsaBroker** | Audit entity. |
| `RequestTypeDefinition` | `ElsaBroker.Data.Entities` | **ElsaBroker** | DB-sourced config entity. |
| `RequestTypeRegistry` | `ElsaBroker.Data.Registry` | **ElsaBroker** | Runtime config cache. |
| `RequestTypeModel` | `ElsaBroker.Data.Registry` | **ElsaBroker** | Registry entry DTO. |
| `RegistryLoader` | `ElsaBroker.Data.Registry` | **ElsaBroker** | JSON + DB merge logic. |
| `WorkflowRegistry` | `ElsaBroker.Data.Registry` | **ElsaBroker** | Request-type to workflow mapping. |
| `WorkflowFolderScanner` | `ElsaBroker.Data.Registry` | **ElsaBroker** | Scans workflow JSON files. |
| `WorkflowRegistration` | `ElsaBroker.Data.Registry` | **ElsaBroker** | Scanner result record. |
| `ConsoleLogShipper` | `ElsaBroker.Data.Logging` | **ElsaBroker** | Built-in shipper. |
| `HttpJsonLogShipper` | `ElsaBroker.Data.Logging` | **ElsaBroker** | Built-in shipper. |
| `HttpJsonLogShipperOptions` | `ElsaBroker.Data.Logging` | **ElsaBroker** | Shipper config. |
| `CompositeLogShipper` | `ElsaBroker.Data.Logging` | **ElsaBroker** | Fan-out shipper. |
| `NullLogShipper` | `ElsaBroker.Data.Logging` | **ElsaBroker** | No-op default. |
| `LogShipperExtensions` | `ElsaBroker.Data.Logging` | **ElsaBroker** | DI helpers for shippers. |
| `SubmitRequestConsumer` | `ElsaBroker.Processor` | *not packaged* | Host-internal consumer. |
| `ElsaDispatchHandler` | `ElsaBroker.Processor` | *not packaged* | Host-internal handler. |
| `ElsaDispatchOptions` | `ElsaBroker.Processor` | *not packaged* | Host-internal config. |
| `MtlsAuthHandler` | `ElsaBroker.Queue` | *not packaged* | Host-internal auth. |
| `RequestEndpoints` | `ElsaBroker.Queue` | *not packaged* | Host-internal API. |

## Implementation Plan

The restructuring required to realize Option B is mechanical and can be completed in a single task:

1. **Create `ElsaBroker.Abstractions` project** (`net9.0` class library, `IsPackable=true`).
   - Add a `ProjectReference` to `ElsaBroker.Contracts`.
   - Move `ILogShipper.cs` and `BrokerLogEvent.cs` from `ElsaBroker.Data/Logging/`.
   - Move `IRequestHandler.cs` (which contains both `IRequestHandler` and `RequestResult`) from
     `ElsaBroker.Processor/Handlers/`.
   - Update namespaces to `ElsaBroker.Abstractions` (or keep originals via `RootNamespace` --
     prefer the new namespace for clarity).

2. **Update `ElsaBroker.Data`** to reference `ElsaBroker.Abstractions` instead of defining the
   logging interfaces itself. Update `using` directives in shipper implementations and
   `LogShipperExtensions`.

3. **Update `ElsaBroker.Processor`** to reference `ElsaBroker.Abstractions`. Update `using` directives
   in `SubmitRequestConsumer`, `ElsaDispatchHandler`, and the legacy handler stubs.

4. **Set `<IsPackable>false</IsPackable>`** on Queue, Processor, WorkflowServer, CertTools, and Tests
   (Tests already has it; add to the others).

5. **Add `<IsPackable>true</IsPackable>`** and NuGet metadata (`PackageId`, `Description`, `Authors`,
   `PackageLicenseExpression`, `RepositoryUrl`) to Contracts, Abstractions, and the root
   ElsaBroker library project. This can live in `Directory.Build.props` for shared metadata.

6. **Verify:** `dotnet pack` produces exactly three `.nupkg` files. A scratch project referencing only
   `ElsaBroker.Abstractions` compiles an `ILogShipper` implementation with no EF/MassTransit on the
   dependency graph.

## More Information

* This ADR is the key deliverable of backlog item **EB-004**.
* The `ElsaBroker.Abstractions` namespace should be `ElsaBroker.Abstractions` (not the original
  `ElsaBroker.Data.Logging` / `ElsaBroker.Processor.Handlers`) to signal that these types belong to
  the extensibility surface, not to an internal project. Type-forwarding attributes
  (`[assembly: TypeForwardedTo(...)]`) in Data and Processor can preserve binary compatibility for any
  early adopters, though pre-1.0 this is a courtesy rather than a requirement.
* Versioning strategy pre-1.0: all three packages share the same version number (e.g. `0.2.0`).
  Post-1.0, `ElsaBroker.Contracts` may diverge to a slower cadence if the wire contract stabilizes
  ahead of the infrastructure package.
* If a future ADR introduces a `dotnet new elsa-broker` template pack, the templates reference the
  `ElsaBroker` meta-package and ship as a separate NuGet tool package
  (`ElsaBroker.Templates`), outside the scope of this decision.
