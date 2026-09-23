namespace Isis.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Merges a scope's instructions onto the tenant-global set to produce the effective, ordered list an
    /// agent should see. Matching is by instruction name: a scope instruction appends a new item, replaces the
    /// content of a same-named global item (in place), or hides a same-named global item. Pure and
    /// side-effect free so it can be unit tested directly.
    /// </summary>
    public static class InstructionResolver
    {
        #region Public-Methods

        /// <summary>
        /// Resolve the effective instructions for a scope.
        /// </summary>
        /// <param name="globalInstructions">The tenant-global instructions (scope id null).</param>
        /// <param name="scopeInstructions">The scope-specific instructions; may be empty.</param>
        /// <param name="scopeId">The scope being resolved (stamped onto the results); may be null.</param>
        /// <returns>The merged, active, position-sequenced effective instructions.</returns>
        public static List<ResolvedInstruction> Resolve(IEnumerable<Instruction> globalInstructions, IEnumerable<Instruction> scopeInstructions, string? scopeId)
        {
            if (globalInstructions == null) throw new ArgumentNullException(nameof(globalInstructions));
            if (scopeInstructions == null) throw new ArgumentNullException(nameof(scopeInstructions));

            List<Instruction> globals = globalInstructions.Where(g => g.Active).OrderBy(g => g.Position).ThenBy(g => g.CreatedUtc).ToList();
            List<Instruction> scoped = scopeInstructions.Where(s => s.Active).OrderBy(s => s.Position).ThenBy(s => s.CreatedUtc).ToList();

            List<ResolvedInstruction> effective = new List<ResolvedInstruction>();
            foreach (Instruction g in globals)
            {
                effective.Add(new ResolvedInstruction { Id = g.Id, Name = g.Name, Content = g.Content, Position = g.Position, Source = InstructionSourceEnum.Global, ScopeId = scopeId });
            }

            List<ResolvedInstruction> appended = new List<ResolvedInstruction>();
            foreach (Instruction s in scoped)
            {
                switch (s.MergeMode)
                {
                    case InstructionMergeModeEnum.Hide:
                        effective.RemoveAll(e => NameEquals(e.Name, s.Name) && e.Source != InstructionSourceEnum.ScopeAdded);
                        break;

                    case InstructionMergeModeEnum.Replace:
                        ResolvedInstruction? target = effective.FirstOrDefault(e => NameEquals(e.Name, s.Name) && e.Source == InstructionSourceEnum.Global);
                        if (target != null)
                        {
                            target.Content = s.Content;
                            target.Source = InstructionSourceEnum.ScopeOverride;
                            target.Id = s.Id;
                        }
                        else
                        {
                            appended.Add(new ResolvedInstruction { Id = s.Id, Name = s.Name, Content = s.Content, Position = s.Position, Source = InstructionSourceEnum.ScopeAdded, ScopeId = scopeId });
                        }
                        break;

                    case InstructionMergeModeEnum.Append:
                    default:
                        appended.Add(new ResolvedInstruction { Id = s.Id, Name = s.Name, Content = s.Content, Position = s.Position, Source = InstructionSourceEnum.ScopeAdded, ScopeId = scopeId });
                        break;
                }
            }

            effective.AddRange(appended);

            // Re-sequence to a clean 0..n effective order (globals first, appended scope items after).
            for (int i = 0; i < effective.Count; i++) effective[i].Position = i;
            return effective;
        }

        #endregion

        #region Private-Methods

        private static bool NameEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
