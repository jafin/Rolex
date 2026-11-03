using System.Linq;
using F;
using Xunit;

namespace Rolex.Tests
{
    /// <summary>
    /// A pattern is read through <c>LexContext</c>, which used to hand back one UTF-16 unit at a
    /// time. A literal character above U+FFFF in a pattern reached the parser as two surrogates, so
    /// <c>[𐀀-􏿿]</c> became a set holding the two halves and a dash, and the generated table
    /// had no range above U+FFFF. The matcher works on code points, so the pattern has to as well.
    /// </summary>
    public class SupplementaryCharTests
    {
        private const string First = "\U00010000";
        private const string Last = "\U0010FFFF";
        private const string Deseret = "\U00010400";

        private static FA Machine(string regex)
        {
            return FA.Parse(regex, 0, true).ToDfa();
        }

        /// <summary>
        /// Walks the machine one code point at a time, as the generated tokenizer does. <c>FA.Match</c>
        /// feeds UTF-16 units, so it cannot be given a character above U+FFFF.
        /// </summary>
        private static bool Accepts(FA dfa, string text)
        {
            var state = dfa;
            for (var i = 0; i < text.Length;)
            {
                var pair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                var cp = pair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                i += pair ? 2 : 1;
                var next = state.Transitions.Where(t => t.Min <= cp && cp <= t.Max).Select(t => t.To).FirstOrDefault();
                if (next == null)
                    return false;
                state = next;
            }
            return state.IsAccepting;
        }

        [Fact]
        public void A_range_of_literal_supplementary_chars_is_one_range_of_code_points()
        {
            var dfa = Machine("[" + First + "-" + Last + "]");

            var t = Assert.Single(dfa.Transitions);

            Assert.Equal(0x10000, t.Min);
            Assert.Equal(0x10FFFF, t.Max);
        }

        [Theory]
        [InlineData(First, true)]
        [InlineData(Deseret, true)]
        [InlineData(Last, true)]
        [InlineData("a", false)]
        [InlineData("퟿", false)]
        [InlineData("", false)]
        [InlineData("�", false)]
        public void A_literal_supplementary_range_matches_only_supplementary_chars(string input, bool expected)
        {
            Assert.Equal(expected, Accepts(Machine("[" + First + "-" + Last + "]"), input));
        }

        /// <summary>
        /// <c>rolex.exe</c> does not read a rule with <c>FA.Parse</c>. It builds a
        /// <c>RegexExpression</c> tree, which has a parser of its own with the same two reads.
        /// </summary>
        [Fact]
        public void The_regex_tree_reads_a_literal_supplementary_range_as_one_range()
        {
            var dfa = RegexExpression.Parse("[" + First + "-" + Last + "]").ToFA(0).ToDfa();

            var t = Assert.Single(dfa.Transitions);

            Assert.Equal(0x10000, t.Min);
            Assert.Equal(0x10FFFF, t.Max);
        }

        [Fact]
        public void The_regex_tree_reads_a_literal_supplementary_char_as_one_code_point()
        {
            var literal = Assert.IsType<RegexLiteralExpression>(RegexExpression.Parse(Deseret + "b"));

            Assert.Equal(new[] { 0x10400, (int)'b' }, literal.Codepoints);
        }

        [Fact]
        public void The_regex_tree_reads_a_supplementary_char_in_a_set_as_one_code_point()
        {
            var dfa = RegexExpression.Parse("[a" + Deseret + "]").ToFA(0).ToDfa();

            Assert.True(Accepts(dfa, Deseret));
            Assert.True(Accepts(dfa, "a"));
            Assert.False(Accepts(dfa, "\uD801"));
        }

        [Fact]
        public void A_literal_supplementary_char_matches_only_itself()
        {
            var dfa = Machine(Deseret);

            Assert.True(Accepts(dfa, Deseret));
            Assert.False(Accepts(dfa, "\U00010401"));
            Assert.False(Accepts(dfa, "\uD801"));
            Assert.False(Accepts(dfa, "\uDC00"));
        }

        [Fact]
        public void A_supplementary_char_beside_a_bmp_range_stays_separate()
        {
            var dfa = Machine("[a-z" + Deseret + "]+");

            Assert.True(Accepts(dfa, "ab" + Deseret + "c"));
            Assert.False(Accepts(dfa, "ab\U00010401"));
        }

        [Fact]
        public void A_lone_surrogate_in_a_pattern_is_still_read_as_itself()
        {
            var dfa = Machine("\uD801x");

            Assert.True(Accepts(dfa, "\uD801x"));
            Assert.False(Accepts(dfa, Deseret));
        }

        [Fact]
        public void The_set_boundary_after_a_supplementary_char_is_not_swallowed()
        {
            var dfa = Machine("[" + Deseret + "]b");

            Assert.Equal(new[] { 0x10400 }, dfa.Transitions.SelectMany(t => new[] { t.Min }).ToArray());
            Assert.True(Accepts(dfa, Deseret + "b"));
        }
    }
}
