using Typedown.WinUI.Services.Export;
using Xunit;

namespace Caret.ConverterTests
{
    // The callout rule of the Word export (Services/Export/Callouts.cs). The editor has the same rule and the same cases in
    // Typedown.Editor/src/components/Muya/lib/utils/callout.test.js.
    public class CalloutTests
    {
        [Fact]
        public void Reads_the_five_kinds_of_GitHub_in_any_case_with_or_without_a_title()
        {
            Assert.Equal(new Callout("note", ""), Callouts.Parse("[!NOTE]"));
            Assert.Equal(new Callout("tip", ""), Callouts.Parse("[!tip]"));
            Assert.Equal(new Callout("important", ""), Callouts.Parse("[!Important]"));
            Assert.Equal(new Callout("warning", "Breaking change"), Callouts.Parse("[!WARNING] Breaking change"));
            Assert.Equal(new Callout("caution", ""), Callouts.Parse("[!CAUTION]\nthe rest of the paragraph"));
        }

        [Fact]
        public void Reads_the_names_of_Obsidian_as_the_kind_they_mean_and_its_fold_markers()
        {
            Assert.Equal("note", Callouts.Parse("[!info]").Kind);
            Assert.Equal("tip", Callouts.Parse("[!success]").Kind);
            Assert.Equal("caution", Callouts.Parse("[!danger]").Kind);
            Assert.Equal(new Callout("caution", "Known issue"), Callouts.Parse("[!bug]- Known issue"));
        }

        [Fact]
        public void Is_not_a_callout_when_the_line_is_not_a_marker_of_a_known_kind()
        {
            Assert.Null(Callouts.Parse("A quote"));
            Assert.Null(Callouts.Parse("[!SOMETHING]"));
            Assert.Null(Callouts.Parse("see [!NOTE] later"));
            Assert.Null(Callouts.Parse("[NOTE]"));
            Assert.Null(Callouts.Parse(null));
            Assert.Null(Callouts.Parse("[!constructor]"));
        }

        [Fact]
        public void Measures_the_marker_with_its_fold_sign_and_spaces()
        {
            Assert.Equal(7, Callouts.MarkerLength("[!NOTE]"));
            Assert.Equal(9, Callouts.MarkerLength("[!bug]-  Known issue"));
            Assert.Equal(0, Callouts.MarkerLength("A quote"));
            Assert.Equal(0, Callouts.MarkerLength(null));
        }
    }
}
