"""Regression checks for forbidden references in C# source-level architecture rules."""
import re
import unittest

from csharp_source import mask_non_code

FORBIDDEN = re.compile(r'(?<![\w.])(?:global::)?(?:RabbitMQ\s*\.\s*Client\b|Aspire\s*\.)')


class CSharpSourceTests(unittest.TestCase):
    def assert_violation(self, snippet: str) -> None:
        self.assertIsNotNone(FORBIDDEN.search(mask_non_code(snippet)), snippet)

    def assert_allowed(self, snippet: str) -> None:
        self.assertIsNone(FORBIDDEN.search(mask_non_code(snippet)), snippet)

    def test_executable_interpolations_are_not_masked(self) -> None:
        self.assert_violation('var text = $"{typeof(RabbitMQ.Client.IConnection)}";')
        self.assert_violation('var text = $@"prefix {typeof(Aspire.Hosting.ApplicationModel.IResource)}";')
        self.assert_violation('var text = @$"prefix {typeof(RabbitMQ.Client.IConnection)}";')
        self.assert_violation('var text = $"{new { Value = typeof(RabbitMQ.Client.IConnection) }}";')
        self.assert_violation('var text = $"{($"{typeof(RabbitMQ.Client.IConnection)}")}";')
        self.assert_violation('var text = $"""prefix {typeof(RabbitMQ.Client.IConnection)} suffix""";')
        self.assert_violation('var text = $$"""prefix {{typeof(Aspire.Hosting.ApplicationModel.IResource)}} suffix""";')

    def test_literally_identical_text_is_not_executable(self) -> None:
        self.assert_allowed('var text = "typeof(RabbitMQ.Client.IConnection)";')
        self.assert_allowed('var text = @"typeof(RabbitMQ.Client.IConnection)";')
        self.assert_allowed('var text = """typeof(RabbitMQ.Client.IConnection)""";')
        self.assert_allowed('var text = $"literal RabbitMQ.Client.IConnection and {{Aspire.Hosting}}";')
        self.assert_allowed('// RabbitMQ.Client.IConnection\nvar number = 1;')
        self.assert_allowed('/* Aspire.Hosting */ var number = 1;')
        self.assert_allowed("var text = 'x'; // RabbitMQ.Client.IConnection")
        self.assert_allowed('var text = $"{\"RabbitMQ.Client.IConnection\"}";')

    def test_direct_using_alias_global_using_and_fully_qualified_types(self) -> None:
        for statement in (
            'using Rabbit = RabbitMQ.Client;',
            'global using Rabbit = RabbitMQ.Client;',
            'var channel = typeof(global::RabbitMQ.Client.IConnection);',
            'using Aspire.Hosting.ApplicationModel;',
        ):
            with self.subTest(statement=statement):
                self.assert_violation(statement)

    def test_diagnostics_keep_original_line_numbers(self) -> None:
        snippet = 'var text = $"ignored RabbitMQ.Client {12}";\n// Aspire.Hosting\nvar t = typeof(RabbitMQ.Client.IConnection);'
        cleaned = mask_non_code(snippet)
        self.assertEqual(snippet.count("\n"), cleaned.count("\n"))
        self.assertEqual(snippet.index("var t = typeof"), cleaned.index("var t = typeof"))
        self.assertEqual(3, cleaned.count("\n", 0, FORBIDDEN.search(cleaned).start()) + 1)


if __name__ == "__main__":
    unittest.main()
