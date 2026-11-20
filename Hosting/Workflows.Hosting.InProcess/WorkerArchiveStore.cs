using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Workflows.Hosting.InProcess
{
    /// <summary>
    /// A worker build preserved on disk after its version stopped being served.
    /// </summary>
    public class ArchivedWorker
    {
        public string Version { get; set; } = string.Empty;

        /// <summary>Directory containing the archived publish output.</summary>
        public string Directory { get; set; } = string.Empty;

        /// <summary>Entry point inside <see cref="Directory"/> (<c>Workflows.Runner.dll</c> or <c>.exe</c>).</summary>
        public string ExecutableName { get; set; } = string.Empty;

        /// <summary>Runtime identifier of the archived publish, when known.</summary>
        public string RuntimeIdentifier { get; set; } = string.Empty;

        /// <summary>Directory the build was published to before it was archived.</summary>
        public string SourceDirectory { get; set; } = string.Empty;

        public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;

        public string ExecutablePath => Path.Combine(Directory, ExecutableName);
    }

    /// <summary>
    /// Keeps retired worker builds around so a late compensation - or any instance that still routes to a
    /// version the host no longer ships - can respawn a worker that matches its own runtime and dependencies.
    /// A shared runtime stops being a valid fallback once the host moves to a newer TFM, so each version is
    /// archived as a self-contained publish.
    /// </summary>
    public class WorkerArchiveStore
    {
        /// <summary>Overrides the archive root directory.</summary>
        public const string ArchiveRootVariable = "WORKFLOWS_WORKER_ARCHIVE";

        private const string MetadataFileName = "worker-archive.json";

        public WorkerArchiveStore(string? archiveRoot = null)
        {
            ArchiveRoot = archiveRoot
                ?? Environment.GetEnvironmentVariable(ArchiveRootVariable)
                ?? Path.Combine(AppContext.BaseDirectory, "worker-archive");
        }

        public string ArchiveRoot { get; }

        /// <summary>
        /// Copies a published worker build into the archive. Archiving an already archived version replaces the
        /// previous copy, so a re-published build of the same version wins.
        /// </summary>
        public Task<ArchivedWorker> ArchiveAsync(
            string version,
            string sourceDirectory,
            string? runtimeIdentifier = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is required.", nameof(version));
            if (!System.IO.Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Worker publish directory '{sourceDirectory}' does not exist.");
            }

            ct.ThrowIfCancellationRequested();

            string target = Path.Combine(ArchiveRoot, SanitizeVersion(version));
            if (System.IO.Directory.Exists(target))
            {
                System.IO.Directory.Delete(target, recursive: true);
            }
            System.IO.Directory.CreateDirectory(target);

            CopyDirectory(sourceDirectory, target, ct);

            var archived = new ArchivedWorker
            {
                Version = version,
                Directory = target,
                ExecutableName = ResolveExecutableName(target),
                RuntimeIdentifier = runtimeIdentifier ?? string.Empty,
                SourceDirectory = sourceDirectory,
                ArchivedAt = DateTime.UtcNow
            };

            File.WriteAllText(
                Path.Combine(target, MetadataFileName),
                JsonConvert.SerializeObject(archived, Formatting.Indented));

            return Task.FromResult(archived);
        }

        public ArchivedWorker? FindArchivedWorker(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;

            string target = Path.Combine(ArchiveRoot, SanitizeVersion(version));
            string metadataPath = Path.Combine(target, MetadataFileName);
            if (!File.Exists(metadataPath)) return null;

            try
            {
                var archived = JsonConvert.DeserializeObject<ArchivedWorker>(File.ReadAllText(metadataPath));
                if (archived == null) return null;

                archived.Directory = target;
                if (string.IsNullOrEmpty(archived.ExecutableName))
                {
                    archived.ExecutableName = ResolveExecutableName(target);
                }

                return File.Exists(archived.ExecutablePath) ? archived : null;
            }
            catch (Exception)
            {
                // A partially written archive is treated as "not archived" so the caller falls back instead of
                // spawning a broken process.
                return null;
            }
        }

        public bool IsArchived(string version) => FindArchivedWorker(version) != null;

        public IReadOnlyList<ArchivedWorker> ListArchivedWorkers()
        {
            if (!System.IO.Directory.Exists(ArchiveRoot)) return Array.Empty<ArchivedWorker>();

            return System.IO.Directory
                .EnumerateDirectories(ArchiveRoot)
                .Select(dir =>
                {
                    string metadataPath = Path.Combine(dir, MetadataFileName);
                    if (!File.Exists(metadataPath)) return null;
                    try
                    {
                        var archived = JsonConvert.DeserializeObject<ArchivedWorker>(File.ReadAllText(metadataPath));
                        if (archived == null) return null;
                        archived.Directory = dir;
                        return archived;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                })
                .Where(a => a != null)
                .Select(a => a!)
                .OrderBy(a => a.ArchivedAt)
                .ToList();
        }

        private static string SanitizeVersion(string version)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = version.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private static string ResolveExecutableName(string directory)
        {
            foreach (var candidate in new[] { "Workflows.Runner.exe", "Workflows.Runner.dll" })
            {
                if (File.Exists(Path.Combine(directory, candidate))) return candidate;
            }

            // Nothing recognisable: keep the conventional name so the archive can still be inspected.
            return "Workflows.Runner.dll";
        }

        private static void CopyDirectory(string source, string target, CancellationToken ct)
        {
            foreach (var dir in System.IO.Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                System.IO.Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
            }

            foreach (var file in System.IO.Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
            }
        }
    }
}