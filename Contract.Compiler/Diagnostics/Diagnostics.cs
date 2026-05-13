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

        public string? SourceCode { get; set; }

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
            var lines = SourceCode?.Split('\n');

            foreach (var diagnostic in _diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column))
            {
                string color = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => "\u001b[31;1m", // Bold Red
                    DiagnosticSeverity.Warning => "\u001b[33;1m", // Bold Yellow
                    _ => "\u001b[34;1m" // Bold Blue
                };
                string reset = "\u001b[0m";

                Console.WriteLine($"{color}{diagnostic.Severity.ToString().ToLower()}{reset}: {diagnostic.Message}");
                
                string fileInfo = diagnostic.SourceFile ?? "source";
                Console.WriteLine($"  \u001b[34m-->\u001b[0m {fileInfo}:{diagnostic.Line}:{diagnostic.Column}");

                if (lines != null && diagnostic.Line > 0 && diagnostic.Line <= lines.Length)
                {
                    string sourceLine = lines[diagnostic.Line - 1].TrimEnd();
                    string padding = new string(' ', diagnostic.Line.ToString().Length);
                    
                    Console.WriteLine($"\u001b[34m{padding} |\u001b[0m");
                    Console.WriteLine($"\u001b[34m{diagnostic.Line} |\u001b[0m {sourceLine}");
                    
                    string pointer = new string(' ', Math.Max(0, diagnostic.Column - 1)) + "^";
                    Console.WriteLine($"\u001b[34m{padding} |\u001b[0m {color}{pointer}{reset}");
                }
                Console.WriteLine();
            }
        }
    }
}