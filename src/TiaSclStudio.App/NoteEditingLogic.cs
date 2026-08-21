using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    internal static class NoteEditingLogic
    {
        private const int MaximumTextLength = 2000;

        internal static NoteNode Create(string text, double x, double y)
        {
            string error;
            if (!TryValidateText(text, out error))
            {
                throw new InvalidOperationException(error);
            }

            var normalized = text.Trim();
            return new NoteNode
            {
                Text = normalized,
                Title = CreateTitle(normalized),
                X = x,
                Y = y
            };
        }

        internal static void Apply(DiagramProject project, Guid nodeId, string text)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            string error;
            if (!TryValidateText(text, out error))
            {
                throw new InvalidOperationException(error);
            }

            var matches = (project.Sheets ?? new List<CallSheet>())
                .Where(sheet => sheet != null)
                .SelectMany(sheet => sheet.Nodes ?? new List<CallNode>())
                .OfType<NoteNode>()
                .Where(node => node.Id == nodeId)
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    matches.Count == 0
                        ? "Редактируемая заметка больше не существует."
                        : "Идентификатор заметки не уникален.");
            }

            var normalized = text.Trim();
            matches[0].Text = normalized;
            matches[0].Title = CreateTitle(normalized);
        }

        internal static bool TryValidateText(string text, out string error)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Введите текст заметки.";
                return false;
            }

            if (text.Length > MaximumTextLength)
            {
                error = "Текст заметки не должен превышать " + MaximumTextLength + " символов.";
                return false;
            }

            try
            {
                XmlConvert.VerifyXmlChars(text);
            }
            catch (XmlException)
            {
                error = "Текст содержит недопустимый управляющий символ.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string CreateTitle(string text)
        {
            var line = text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)[0]
                .Trim();
            return line.Length <= 48 ? line : line.Substring(0, 45) + "...";
        }
    }
}
