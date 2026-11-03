using System.Collections.Generic;
using System.Linq;
using F;
using Xunit;
using SysRegex = System.Text.RegularExpressions.Regex;

namespace Rolex.Tests
{
    /// <summary>
    /// <c>x?</c> is built by <c>FA.Optional</c>. It used to add an epsilon move from the start
    /// state to the accepting states of <c>x</c>, so skipping <c>x</c> meant standing in the state
    /// <c>x</c> ends in, with that state's outgoing transitions. When <c>x</c> ends in a loop, as
    /// <c>(ab*)</c> does, the start state inherited the loop: <c>(ab*)?a</c> accepted <c>ba</c>.
    /// A skipped group has to reach a state of its own.
    /// </summary>
    public class OptionalTests
    {
        private const int Accepted = 0;

        private static bool Accepts(FA dfa, string text)
        {
            return dfa.Match(text) == Accepted;
        }

        [Theory]
        [InlineData("(ab*)?a", "ba")]
        [InlineData("(ab*)?a", "bba")]
        [InlineData("(ab*)?c", "bc")]
        [InlineData("((ab*)?a)?", "ba")]
        [InlineData("(a(a|b)*)?a", "ba")]
        [InlineData("(a+b*)?c", "bc")]
        public void A_skipped_group_does_not_keep_the_loop_it_ends_in(string regex, string rejected)
        {
            var dfa = FA.Parse(regex, 0, true).ToDfa();

            Assert.False(Accepts(dfa, rejected), regex + " accepted " + rejected);
        }

        [Fact]
        public void The_start_state_only_leaves_on_what_begins_the_expression()
        {
            var dfa = FA.Parse("(ab*)?a", 0, true).ToDfa();

            var leaving = dfa.Transitions.SelectMany(t => Enumerable.Range(t.Min, t.Max - t.Min + 1)).Distinct().ToList();

            Assert.Equal(new[] { (int)'a' }, leaving);
        }

        [Theory]
        [InlineData("(ab*)?a", new[] { "a", "aa", "aba", "abba" }, new[] { "", "b", "ab", "ba", "abb" })]
        [InlineData("\\[((a(a| )*)?a)?\\]", new[] { "[]", "[a]", "[aa]", "[a a]", "[a  a]" }, new[] { "[ ]", "[  ]", "[ a]", "[a ]", "[ a ]" })]
        public void Optional_around_a_loop_matches_the_language(string regex, string[] accepted, string[] rejected)
        {
            var dfa = FA.Parse(regex, 0, true).ToDfa();

            foreach (var text in accepted)
                Assert.True(Accepts(dfa, text), regex + " refused " + text);
            foreach (var text in rejected)
                Assert.False(Accepts(dfa, text), regex + " accepted " + text);
        }

        [Fact]
        public void The_requested_accept_id_is_kept_for_the_empty_match()
        {
            var dfa = FA.Optional(new FA(7), 42).ToDfa();

            Assert.Equal(42, dfa.Match(string.Empty));
        }


        /// <summary>
        /// Rolex's pattern syntax is close enough to .NET's for these to be read by both, so the
        /// .NET engine is the oracle: every short string over the alphabet, accepted by both or neither.
        /// </summary>
        [Theory]
        [InlineData("(ab*)?a", "ab")]
        [InlineData("((ab*)?a)?", "ab")]
        [InlineData("(ab*)?(cd*)?e", "abcde")]
        [InlineData("(a(a|b)*)?a", "ab")]
        [InlineData("((ab*)?c)?d", "abcd")]
        [InlineData("(ab*){0,2}c", "abc")]
        [InlineData("(ab*)*c", "abc")]
        [InlineData("(ab*)+c", "abc")]
        [InlineData("\\[((a(a| )*)?a)?\\]", "[a ]")]
        public void Optional_agrees_with_the_dotnet_engine(string regex, string alphabet)
        {
            var dfa = FA.Parse(regex, 0, true).ToDfa();
            var oracle = new SysRegex("^(?:" + regex + ")$");

            var disagreements = new List<string>();
            var depth = alphabet.Length > 3 ? 6 : 7;
            Sweep(string.Empty, alphabet, depth, text =>
            {
                if (Accepts(dfa, text) != oracle.IsMatch(text))
                    disagreements.Add(text);
            });

            Assert.True(disagreements.Count == 0,
                regex + " disagrees with .NET on " + disagreements.Count + " strings, e.g. <" + string.Join(">, <", disagreements.Take(3)) + ">");
        }

        private static void Sweep(string current, string alphabet, int depth, System.Action<string> visit)
        {
            if (current.Length > 0)
                visit(current);
            if (depth == 0)
                return;
            foreach (var c in alphabet)
                Sweep(current + c, alphabet, depth - 1, visit);
        }
    }
}
