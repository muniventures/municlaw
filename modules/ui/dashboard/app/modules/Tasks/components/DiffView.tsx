import * as React from "react";
import { FileCode, Plus, Minus, GitCommit, FileText, CheckCircle2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { DiffFileChange } from "@/api/types";

interface DiffViewProps {
  baseCommitSha?: string;
  reviewedCommitSha?: string;
  files: DiffFileChange[];
}

export function DiffView({
  baseCommitSha,
  reviewedCommitSha,
  files,
}: DiffViewProps) {
  const [selectedFilePath, setSelectedFilePath] = React.useState<string>(
    files[0]?.path || ""
  );

  React.useEffect(() => {
    if (files.length > 0 && !files.some((f) => f.path === selectedFilePath)) {
      setSelectedFilePath(files[0].path);
    }
  }, [files, selectedFilePath]);

  const selectedFile = files.find((f) => f.path === selectedFilePath) || files[0];

  const totalAdditions = files.reduce((acc, f) => acc + f.additions, 0);
  const totalDeletions = files.reduce((acc, f) => acc + f.deletions, 0);

  return (
    <div className="rounded-xl border border-border bg-card overflow-hidden shadow-xs">
      {/* Commit Tuple Header */}
      <div className="p-4 border-b border-border bg-muted/30 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-xs">
        <div className="flex items-center gap-2 flex-wrap">
          <GitCommit className="h-4 w-4 text-muted-foreground" />
          <span className="font-semibold text-foreground">Review Tuple:</span>
          <span className="font-mono bg-background px-2 py-0.5 rounded border border-border">
            Base: {baseCommitSha ? baseCommitSha.slice(0, 8) : "None"}
          </span>
          <span className="text-muted-foreground">→</span>
          <span className="font-mono bg-background px-2 py-0.5 rounded border border-border">
            Reviewed: {reviewedCommitSha ? reviewedCommitSha.slice(0, 8) : "Pending"}
          </span>
        </div>

        <div className="flex items-center gap-3 font-mono">
          <span className="text-emerald-600 font-semibold flex items-center gap-0.5">
            <Plus className="h-3 w-3" />
            {totalAdditions} additions
          </span>
          <span className="text-rose-600 font-semibold flex items-center gap-0.5">
            <Minus className="h-3 w-3" />
            {totalDeletions} deletions
          </span>
          <span className="text-muted-foreground">
            ({files.length} {files.length === 1 ? "file" : "files"})
          </span>
        </div>
      </div>

      {files.length === 0 ? (
        <div className="p-12 text-center text-muted-foreground">
          <FileText className="h-8 w-8 mx-auto mb-2 opacity-50" />
          <p>No changed files recorded for this run yet.</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-12 min-h-[400px]">
          {/* File sidebar list */}
          <div className="md:col-span-4 border-r border-border p-2 space-y-1 overflow-y-auto max-h-[500px]">
            <div className="px-2 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Modified Files
            </div>
            {files.map((file) => {
              const isSelected = file.path === selectedFile?.path;
              return (
                <button
                  key={file.path}
                  type="button"
                  onClick={() => setSelectedFilePath(file.path)}
                  className={`w-full flex items-center justify-between gap-2 px-2.5 py-2 rounded-md text-xs font-mono transition-colors text-left cursor-pointer ${
                    isSelected
                      ? "bg-primary text-primary-foreground font-semibold"
                      : "hover:bg-muted text-muted-foreground hover:text-foreground"
                  }`}
                >
                  <div className="flex items-center gap-2 truncate">
                    <FileCode className="h-3.5 w-3.5 shrink-0" />
                    <span className="truncate">{file.path}</span>
                  </div>
                  <div className="flex items-center gap-1.5 shrink-0 text-[10px]">
                    <span className={isSelected ? "text-primary-foreground" : "text-emerald-600"}>
                      +{file.additions}
                    </span>
                    <span className={isSelected ? "text-primary-foreground" : "text-rose-600"}>
                      -{file.deletions}
                    </span>
                  </div>
                </button>
              );
            })}
          </div>

          {/* Unified Diff content */}
          <div className="md:col-span-8 flex flex-col bg-muted/10 overflow-hidden">
            {selectedFile && (
              <>
                <div className="p-3 border-b border-border bg-card flex items-center justify-between text-xs">
                  <div className="flex items-center gap-2 font-mono font-medium">
                    <FileCode className="h-4 w-4 text-primary" />
                    <span>{selectedFile.path}</span>
                    <Badge
                      variant={
                        selectedFile.status === "added"
                          ? "success"
                          : selectedFile.status === "deleted"
                          ? "destructive"
                          : "outline"
                      }
                      className="text-[10px] uppercase font-mono ml-2"
                    >
                      {selectedFile.status}
                    </Badge>
                  </div>
                </div>

                <div className="p-4 font-mono text-xs overflow-x-auto max-h-[460px] bg-background">
                  {selectedFile.patch ? (
                    <div className="space-y-0.5">
                      {selectedFile.patch.split("\n").map((line, idx) => {
                        let lineClass = "text-muted-foreground";
                        let bgClass = "";
                        if (line.startsWith("+") && !line.startsWith("+++")) {
                          lineClass = "text-emerald-700 dark:text-emerald-400";
                          bgClass = "bg-emerald-500/10";
                        } else if (line.startsWith("-") && !line.startsWith("---")) {
                          lineClass = "text-rose-700 dark:text-rose-400";
                          bgClass = "bg-rose-500/10";
                        } else if (line.startsWith("@@")) {
                          lineClass = "text-blue-600 dark:text-blue-400 font-semibold";
                          bgClass = "bg-blue-500/10";
                        }

                        return (
                          <div
                            key={idx}
                            className={`px-2 py-0.5 rounded-xs whitespace-pre ${bgClass} ${lineClass}`}
                          >
                            {line}
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <div className="p-6 text-center text-muted-foreground">
                      <CheckCircle2 className="h-6 w-6 mx-auto mb-1 text-emerald-500 opacity-60" />
                      <p>Diff preview ready for review.</p>
                      <p className="text-[11px] mt-1 font-mono text-muted-foreground">
                        +{selectedFile.additions} / -{selectedFile.deletions} lines modified.
                      </p>
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
