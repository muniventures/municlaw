import * as React from "react";
import { Send, CornerDownLeft } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";

interface FollowUpInputProps {
  onSendFollowUp: (instruction: string) => Promise<void>;
  disabled?: boolean;
}

export function FollowUpInput({ onSendFollowUp, disabled }: FollowUpInputProps) {
  const [instruction, setInstruction] = React.useState("");
  const [isSubmitting, setIsSubmitting] = React.useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!instruction.trim() || isSubmitting || disabled) return;

    try {
      setIsSubmitting(true);
      await onSendFollowUp(instruction.trim());
      setInstruction("");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="relative rounded-xl border border-border bg-card p-3 shadow-xs">
      <label htmlFor="followup-instruction" className="sr-only">
        Follow-up Instruction
      </label>
      <Textarea
        id="followup-instruction"
        placeholder={
          disabled
            ? "Waiting for the current task run to finish or pause before sending follow-up..."
            : "Send follow-up turn or feedback (e.g., 'Also add unit tests for edge cases')... Press ⌘+Enter to send"
        }
        rows={3}
        value={instruction}
        onChange={(e) => setInstruction(e.target.value)}
        onKeyDown={handleKeyDown}
        disabled={disabled || isSubmitting}
        className="resize-none border-0 shadow-none focus-visible:ring-0 p-1 text-sm bg-transparent placeholder:text-muted-foreground"
      />

      <div className="flex items-center justify-between pt-2 border-t border-border/50 text-xs text-muted-foreground">
        <span className="hidden sm:inline text-[11px] font-mono">
          <CornerDownLeft className="h-3 w-3 inline mr-1" />
          Press ⌘+Enter to submit
        </span>

        <Button
          type="submit"
          size="sm"
          disabled={disabled || isSubmitting || !instruction.trim()}
          className="ml-auto gap-1.5"
        >
          <Send className="h-3.5 w-3.5" />
          <span>{isSubmitting ? "Dispatching..." : "Send Turn"}</span>
        </Button>
      </div>
    </form>
  );
}
