using System;
using System.Text.RegularExpressions;

namespace Typedown.WinUI.Services.XmlEditing
{
    // The words of the schema check: the message of the parser (English) is read into a kind and the names it mentions (XmlSchemaWords.Classify),
    // and each kind is said in the language of the person ("The element price is not expected here (inside book). Expected: title."). A message
    // that is not recognised is shown as the parser wrote it, so nothing is hidden.
    internal sealed class XmlSchemaWords
    {
        // ---- reading the messages of the parser -----------------------------------------------------------------------------------------

        private static readonly Regex NotExpected = new(@"^The element '(?<parent>[^']*)' has invalid child element '(?<child>[^']*)'(?: in namespace '[^']*')?\.(?: List of possible elements expected: (?<exp>.+?)\.?)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex Incomplete = new(@"^The element '(?<e>[^']*)' has incomplete content\.(?: List of possible elements expected: (?<exp>.+?)\.?)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex TextNotAllowed = new(@"^The element '(?<e>[^']*)' cannot contain text\.(?: List of possible elements expected: (?<exp>.+?)\.?)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex MissingAttribute = new(@"^The required attribute '(?<a>[^']*)' is missing\.?$", RegexOptions.CultureInvariant);
        private static readonly Regex AttributeNotDeclared = new(@"^The '(?<a>[^']*)' attribute is not declared\.?$", RegexOptions.CultureInvariant);
        private static readonly Regex ElementNotDeclared = new(@"^The '(?<e>[^']*)' element is not declared\.?$", RegexOptions.CultureInvariant);
        private static readonly Regex InvalidValue = new(@"^The '(?<n>[^']*)' (?<what>element|attribute) is invalid - The value '(?<v>.*?)' is invalid according to its datatype '(?<t>[^']*)'(?: - (?<d>.*))?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex InNamespace = new(@"\s+in namespace\s+'[^']*'", RegexOptions.CultureInvariant);

        // `current`: the element the parser was at when it said it (for an attribute, the element the attribute is on).
        internal static XmlSchemaIssue Classify(string message, int line, int position, string current)
        {
            message = (message ?? "").Trim();
            Match m;
            if ((m = NotExpected.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.NotExpected, Line = line, Position = position, Raw = message, Parent = m.Groups["parent"].Value, Element = m.Groups["child"].Value, Expected = Names(m.Groups["exp"]) };
            if ((m = Incomplete.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.Incomplete, Line = line, Position = position, Raw = message, Element = m.Groups["e"].Value, Expected = Names(m.Groups["exp"]) };
            if ((m = TextNotAllowed.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.TextNotAllowed, Line = line, Position = position, Raw = message, Element = m.Groups["e"].Value, Expected = Names(m.Groups["exp"]) };
            if ((m = MissingAttribute.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.MissingAttribute, Line = line, Position = position, Raw = message, Attribute = m.Groups["a"].Value, Element = current };
            if ((m = AttributeNotDeclared.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.AttributeNotDeclared, Line = line, Position = position, Raw = message, Attribute = m.Groups["a"].Value, Element = current };
            if ((m = ElementNotDeclared.Match(message)).Success)
                return new XmlSchemaIssue { Kind = XmlIssueKind.ElementNotDeclared, Line = line, Position = position, Raw = message, Element = m.Groups["e"].Value };
            if ((m = InvalidValue.Match(message)).Success)
                return new XmlSchemaIssue
                {
                    Kind = XmlIssueKind.InvalidValue, Line = line, Position = position, Raw = message,
                    Element = m.Groups["what"].Value == "element" ? m.Groups["n"].Value : current, Attribute = m.Groups["what"].Value == "attribute" ? m.Groups["n"].Value : null,
                    OfAttribute = m.Groups["what"].Value == "attribute", Value = m.Groups["v"].Value, Type = TypeName(m.Groups["t"].Value), Constraint = ConstraintOf(m.Groups["d"].Value),
                };
            return new XmlSchemaIssue { Kind = XmlIssueKind.Other, Line = line, Position = position, Raw = message };
        }

        // "'title, author'" or "'title' in namespace 'urn:x'": the names, without the quotes and the namespaces.
        private static string Names(Group list)
        {
            if (!list.Success) return null;
            var text = InNamespace.Replace(list.Value, "").Replace("'", "").Trim().TrimEnd('.').Trim();
            return text.Length == 0 ? null : text;
        }

        // The datatype as the parser names it ("Decimal", "Date", "String") in the way schemas write it.
        // (the parser names a type with its namespace: "http://www.w3.org/2001/XMLSchema:decimal")
        private static string TypeName(string type)
        {
            type = type.Substring(Math.Max(type.LastIndexOf(':'), type.LastIndexOf('}')) + 1);
            return type.Length == 0 ? type : char.ToLowerInvariant(type[0]) + type.Substring(1);
        }

        private static string ConstraintOf(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            if (detail.Contains("Enumeration constraint", StringComparison.Ordinal)) return "enumeration";
            if (detail.Contains("Pattern constraint", StringComparison.Ordinal)) return "pattern";
            if (detail.Contains("Length constraint", StringComparison.Ordinal)) return "length";
            if (detail.Contains("InclusiveConstraint", StringComparison.Ordinal) || detail.Contains("ExclusiveConstraint", StringComparison.Ordinal)
                || detail.Contains("Inclusive constraint", StringComparison.Ordinal) || detail.Contains("Exclusive constraint", StringComparison.Ordinal)) return "range";
            if (detail.Contains("TotalDigits", StringComparison.Ordinal) || detail.Contains("FractionDigits", StringComparison.Ordinal)) return "digits";
            return null;
        }

        // ---- saying them -------------------------------------------------------------------------------------------------------------

        // "{x}" is replaced by the name or the value, which is shown between quotation marks.
        public string NotExpectedText, NotExpectedInside, ExpectedList;
        public string IncompleteText, TextNotAllowedText;
        public string MissingAttributeOn, MissingAttributeText, AttributeNotAllowedOn, AttributeNotAllowedText, ElementNotDeclaredText;
        public string InvalidElementValue, InvalidAttributeValue;
        public string ConstraintEnumeration, ConstraintPattern, ConstraintLength, ConstraintRange, ConstraintDigits;

        private static string Q(string name) => "\"" + name + "\"";

        public string Say(XmlSchemaIssue i)
        {
            string text;
            switch (i.Kind)
            {
                case XmlIssueKind.NotExpected:
                    text = Fill(NotExpectedText, ("child", Q(i.Element)), ("parent", Q(i.Parent))) + ExpectedSentence(i.Expected);
                    break;
                case XmlIssueKind.Incomplete:
                    text = Fill(IncompleteText, ("e", Q(i.Element))) + ExpectedSentence(i.Expected);
                    break;
                case XmlIssueKind.TextNotAllowed:
                    text = Fill(TextNotAllowedText, ("e", Q(i.Element))) + ExpectedSentence(i.Expected);
                    break;
                case XmlIssueKind.MissingAttribute:
                    text = string.IsNullOrEmpty(i.Element) ? Fill(MissingAttributeText, ("a", Q(i.Attribute))) : Fill(MissingAttributeOn, ("e", Q(i.Element)), ("a", Q(i.Attribute)));
                    break;
                case XmlIssueKind.AttributeNotDeclared:
                    text = string.IsNullOrEmpty(i.Element) ? Fill(AttributeNotAllowedText, ("a", Q(i.Attribute))) : Fill(AttributeNotAllowedOn, ("e", Q(i.Element)), ("a", Q(i.Attribute)));
                    break;
                case XmlIssueKind.ElementNotDeclared:
                    text = Fill(ElementNotDeclaredText, ("e", Q(i.Element)));
                    break;
                case XmlIssueKind.InvalidValue:
                    text = Fill(i.OfAttribute ? InvalidAttributeValue : InvalidElementValue, ("v", Q(i.Value)), ("n", Q(i.OfAttribute ? i.Attribute : i.Element)), ("t", i.Type)) + ConstraintSentence(i.Constraint);
                    break;
                default:
                    return i.Raw;
            }
            return text;
        }

        private string ExpectedSentence(string expected) => string.IsNullOrEmpty(expected) ? "" : Fill(ExpectedList, ("list", expected));

        private string ConstraintSentence(string constraint) => constraint switch
        {
            "enumeration" => ConstraintEnumeration,
            "pattern" => ConstraintPattern,
            "length" => ConstraintLength,
            "range" => ConstraintRange,
            "digits" => ConstraintDigits,
            _ => "",
        };

        private static string Fill(string template, params (string Name, string Value)[] values)
        {
            foreach (var (name, value) in values) template = template.Replace("{" + name + "}", value ?? "");
            return template;
        }

        // English, Spanish, French, Polish and Portuguese (Brazil); other languages read the English words, as the readable views of the packs do.
        public static XmlSchemaWords For(string language)
        {
            var code = (language ?? "en").Trim().ToLowerInvariant();
            if (code.StartsWith("es", StringComparison.Ordinal)) return Spanish;
            if (code.StartsWith("fr", StringComparison.Ordinal)) return French;
            if (code.StartsWith("pl", StringComparison.Ordinal)) return Polish;
            if (code.StartsWith("pt", StringComparison.Ordinal)) return Portuguese;
            return English;
        }

        private static readonly XmlSchemaWords English = new()
        {
            NotExpectedText = "The element {child} is not expected here (inside {parent}).", ExpectedList = " Expected: {list}.",
            IncompleteText = "The element {e} is incomplete: something is missing at the end.", TextNotAllowedText = "The element {e} cannot contain text here.",
            MissingAttributeOn = "The element {e} is missing its required attribute {a}.", MissingAttributeText = "The required attribute {a} is missing.",
            AttributeNotAllowedOn = "The attribute {a} is not allowed on the element {e}.", AttributeNotAllowedText = "The attribute {a} is not allowed here.",
            ElementNotDeclaredText = "The element {e} is not declared in the schema.",
            InvalidElementValue = "The value {v} of the element {n} is not valid: it must be of type {t}.", InvalidAttributeValue = "The value {v} of the attribute {n} is not valid: it must be of type {t}.",
            ConstraintEnumeration = " It is not one of the allowed values.", ConstraintPattern = " It does not follow the required pattern.", ConstraintLength = " Its length is not allowed.",
            ConstraintRange = " It is outside the allowed range.", ConstraintDigits = " It has too many digits.",
        };

        private static readonly XmlSchemaWords Spanish = new()
        {
            NotExpectedText = "El elemento {child} no se espera aquí (dentro de {parent}).", ExpectedList = " Se esperaba: {list}.",
            IncompleteText = "El elemento {e} está incompleto: falta algo al final.", TextNotAllowedText = "El elemento {e} no puede contener texto aquí.",
            MissingAttributeOn = "Al elemento {e} le falta el atributo obligatorio {a}.", MissingAttributeText = "Falta el atributo obligatorio {a}.",
            AttributeNotAllowedOn = "El atributo {a} no está permitido en el elemento {e}.", AttributeNotAllowedText = "El atributo {a} no está permitido aquí.",
            ElementNotDeclaredText = "El elemento {e} no está declarado en el esquema.",
            InvalidElementValue = "El valor {v} del elemento {n} no es válido: debe ser de tipo {t}.", InvalidAttributeValue = "El valor {v} del atributo {n} no es válido: debe ser de tipo {t}.",
            ConstraintEnumeration = " No es uno de los valores permitidos.", ConstraintPattern = " No sigue el patrón exigido.", ConstraintLength = " Su longitud no está permitida.",
            ConstraintRange = " Está fuera del rango permitido.", ConstraintDigits = " Tiene demasiados dígitos.",
        };

        private static readonly XmlSchemaWords French = new()
        {
            NotExpectedText = "L'élément {child} n'est pas attendu ici (dans {parent}).", ExpectedList = " Attendu : {list}.",
            IncompleteText = "L'élément {e} est incomplet : il manque quelque chose à la fin.", TextNotAllowedText = "L'élément {e} ne peut pas contenir de texte ici.",
            MissingAttributeOn = "L'élément {e} n'a pas son attribut obligatoire {a}.", MissingAttributeText = "L'attribut obligatoire {a} est manquant.",
            AttributeNotAllowedOn = "L'attribut {a} n'est pas autorisé sur l'élément {e}.", AttributeNotAllowedText = "L'attribut {a} n'est pas autorisé ici.",
            ElementNotDeclaredText = "L'élément {e} n'est pas déclaré dans le schéma.",
            InvalidElementValue = "La valeur {v} de l'élément {n} n'est pas valide : elle doit être de type {t}.", InvalidAttributeValue = "La valeur {v} de l'attribut {n} n'est pas valide : elle doit être de type {t}.",
            ConstraintEnumeration = " Ce n'est pas l'une des valeurs autorisées.", ConstraintPattern = " Elle ne suit pas le motif exigé.", ConstraintLength = " Sa longueur n'est pas autorisée.",
            ConstraintRange = " Elle est hors de la plage autorisée.", ConstraintDigits = " Elle a trop de chiffres.",
        };

        private static readonly XmlSchemaWords Polish = new()
        {
            NotExpectedText = "Element {child} nie jest tu oczekiwany (wewnątrz {parent}).", ExpectedList = " Oczekiwano: {list}.",
            IncompleteText = "Element {e} jest niekompletny: na końcu czegoś brakuje.", TextNotAllowedText = "Element {e} nie może tu zawierać tekstu.",
            MissingAttributeOn = "Elementowi {e} brakuje wymaganego atrybutu {a}.", MissingAttributeText = "Brakuje wymaganego atrybutu {a}.",
            AttributeNotAllowedOn = "Atrybut {a} nie jest dozwolony w elemencie {e}.", AttributeNotAllowedText = "Atrybut {a} nie jest tu dozwolony.",
            ElementNotDeclaredText = "Element {e} nie jest zadeklarowany w schemacie.",
            InvalidElementValue = "Wartość {v} elementu {n} jest nieprawidłowa: musi być typu {t}.", InvalidAttributeValue = "Wartość {v} atrybutu {n} jest nieprawidłowa: musi być typu {t}.",
            ConstraintEnumeration = " Nie jest to jedna z dozwolonych wartości.", ConstraintPattern = " Nie pasuje do wymaganego wzorca.", ConstraintLength = " Jej długość jest niedozwolona.",
            ConstraintRange = " Jest poza dozwolonym zakresem.", ConstraintDigits = " Ma zbyt wiele cyfr.",
        };

        private static readonly XmlSchemaWords Portuguese = new()
        {
            NotExpectedText = "O elemento {child} não é esperado aqui (dentro de {parent}).", ExpectedList = " Esperado: {list}.",
            IncompleteText = "O elemento {e} está incompleto: falta algo no final.", TextNotAllowedText = "O elemento {e} não pode conter texto aqui.",
            MissingAttributeOn = "Ao elemento {e} falta o atributo obrigatório {a}.", MissingAttributeText = "Falta o atributo obrigatório {a}.",
            AttributeNotAllowedOn = "O atributo {a} não é permitido no elemento {e}.", AttributeNotAllowedText = "O atributo {a} não é permitido aqui.",
            ElementNotDeclaredText = "O elemento {e} não está declarado no esquema.",
            InvalidElementValue = "O valor {v} do elemento {n} não é válido: deve ser do tipo {t}.", InvalidAttributeValue = "O valor {v} do atributo {n} não é válido: deve ser do tipo {t}.",
            ConstraintEnumeration = " Não é um dos valores permitidos.", ConstraintPattern = " Não segue o padrão exigido.", ConstraintLength = " Seu comprimento não é permitido.",
            ConstraintRange = " Está fora do intervalo permitido.", ConstraintDigits = " Tem dígitos demais.",
        };
    }
}
