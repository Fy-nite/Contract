using System.Collections.Generic;

namespace Contract.Compiler.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Error,
        Warning,
        Info
    }

    public class Diagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }
        public string? SourceFile { get; }

        public Diagnostic(DiagnosticSeverity severity, string message, int line, int column, string? sourceFile = null)
        {
            Severity = severity;
            Message = message;
            Line = line;
            Column = column;
            SourceFile = sourceFile;
        }

        public override string ToString()
        {
            var file = SourceFile != null ? $"{SourceFile}:" : "";
            return $"{file}{Line}:{Column}: {Severity.ToString().ToLower()}: {Message}";
        }
    }

    public class DiagnosticBag
    {
        private readonly List<Diagnostic> _diagnostics = new();

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public bool HasErrors => _diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);

        public void AddError(string message, int line, int column, string? sourceFile = null)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, line, column, sourceFile));
        }

        public void AddWarning(string message, int line, int column, string? sourceFile = null)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, message, line, column, sourceFile));
        }

        public void AddInfo(string message, int line, int column, string? sourceFile = null)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info, message, line, column, sourceFile));
        }

        public void ReportToConsole()
        {
            foreach (var diagnostic in _diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column))
            {
                Console.WriteLine(diagnostic.ToString());
            }
        }
    }
}