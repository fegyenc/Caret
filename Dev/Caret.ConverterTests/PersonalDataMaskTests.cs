using Typedown.WinUI.Services.Conversion;
using Xunit;

namespace Caret.ConverterTests
{
    // Edit > Mask personal data (Services/Conversion/PersonalDataMask.cs): the masking of the emails, for any note.
    public class PersonalDataMaskTests
    {
        private const string Note = "# Contract\n\nWrite to anna.nowak@example.com or call +48 601 234 567.\nPay to PL61 1090 1014 0000 0712 1981 2874 (IBAN), card 4111 1111 1111 1111.\nAnna Nowak signs; Anna's colleague Bo Berg too. Anna.nowak@example.com again.\n";

        [Fact]
        public void Addresses_phones_ibans_and_cards_are_replaced_and_counted()
        {
            var result = PersonalDataMask.Mask(Note);
            Assert.DoesNotContain("anna.nowak@example.com", result.Text, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("601 234 567", result.Text);
            Assert.DoesNotContain("PL61", result.Text);
            Assert.DoesNotContain("4111", result.Text);
            Assert.Contains("[EMAIL-1]", result.Text);
            Assert.Contains("[PHONE-1]", result.Text);
            Assert.Contains("[IBAN-1]", result.Text);
            Assert.Contains("[CARD-1]", result.Text);
            Assert.Equal(1, result.Counts["EMAIL"]);   // the same address, written twice, is one value
            Assert.Equal(4, result.Total);
            Assert.StartsWith("# Contract", result.Text);   // the rest of the note is as it was
        }

        [Fact]
        public void The_names_of_the_list_are_masked_and_the_same_person_keeps_one_placeholder()
        {
            var result = PersonalDataMask.Mask(Note, PersonalDataMask.ParseNames("Anna Nowak\n  Bo Berg \n\nA\nanna nowak"));
            Assert.Contains("[PERSON-1] signs", result.Text);
            Assert.DoesNotContain("Bo Berg", result.Text);
            Assert.Equal(2, result.Counts["PERSON"]);
        }

        [Fact]
        public void A_name_is_not_guessed_from_a_greeting_and_a_note_with_nothing_to_mask_is_left_as_it_is()
        {
            const string text = "Hello World,\n\nNothing personal here: order 12345, total 99,90 EUR.\n";
            var result = PersonalDataMask.Mask(text);
            Assert.Equal(text, result.Text);
            Assert.Equal(0, result.Total);
            Assert.Empty(result.Counts);
        }

        [Fact]
        public void The_list_of_names_drops_blank_lines_one_letter_names_and_repeats()
        {
            Assert.Equal(new[] { "Anna Nowak", "Bo Berg" }, PersonalDataMask.ParseNames("Anna Nowak\r\n\r\n x \nBo Berg\nANNA NOWAK\n"));
            Assert.Empty(PersonalDataMask.ParseNames(null));
            // the text box of WinUI ends its lines with a lone \r
            Assert.Equal(new[] { "Anna Nowak", "Bo Berg" }, PersonalDataMask.ParseNames("Anna Nowak\rBo Berg"));
        }

        [Fact]
        public void An_xml_invoice_keeps_its_markup_and_loses_the_ids()
        {
            const string xml = "<NFe><emit><CNPJ>11222333000181</CNPJ><xNome>Loja</xNome><fone>(11) 91234-5678</fone></emit><dest><CPF>52998224725</CPF><email>buyer@shop.example</email></dest></NFe>";
            var result = PersonalDataMask.Mask(xml);
            Assert.Contains("<emit><CNPJ>", result.Text);
            Assert.DoesNotContain("buyer@shop.example", result.Text);
            Assert.DoesNotContain("91234-5678", result.Text);
            Assert.Contains("</NFe>", result.Text);
        }
    }
}
