using LoxTools.Models;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LoxTools.UpdateCheck {
    internal sealed class UpdateCheckXmlParser {
        public UpdateInfo ParseRelease(string xml) {
            return ParseSingle(xml, "Release", UpdateChannel.Release);
        }

        public UpdateInfo ParseBeta(string xml) {
            return ParseOptional(xml, UpdateChannel.Beta, "Beta");
        }

        public UpdateInfo ParseAlpha(string xml) {
            return ParseOptional(xml, UpdateChannel.Alpha, "Alpha", "Test");
        }

        private static UpdateInfo ParseSingle(string xml, string elementName, UpdateChannel channel) {
            XElement element = GetElement(xml, elementName);
            if (element == null) {
                throw new InvalidOperationException($"Missing {elementName} entry in updatecheck.xml.");
            }

            return ParseElement(element, elementName, channel);
        }

        private static UpdateInfo ParseOptional(string xml, UpdateChannel channel, params string[] elementNames) {
            if (elementNames == null || elementNames.Length == 0) {
                return null;
            }

            foreach (string elementName in elementNames) {
                XElement element = GetElement(xml, elementName);
                if (element != null) {
                    return ParseElement(element, elementName, channel);
                }
            }

            return null;
        }

        private static XElement GetElement(string xml, string elementName) {
            if (string.IsNullOrWhiteSpace(xml)) {
                throw new InvalidOperationException("updatecheck.xml content is empty.");
            }

            var settings = new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = UpdateSecurityPolicy.MaxMetadataBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            XDocument doc;
            using (var stringReader = new StringReader(xml))
            using (XmlReader xmlReader = XmlReader.Create(stringReader, settings)) {
                doc = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            return doc.Root?.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase));
        }

        private static UpdateInfo ParseElement(XElement element, string elementName, UpdateChannel channel) {
            string versionString = (string)element.Attribute("Version");
            string path = (string)element.Attribute("Path");
            string crc32 = (string)element.Attribute("crc32");
            string fileSizeRaw = (string)element.Attribute("Filesize");

            if (string.IsNullOrWhiteSpace(versionString)) {
                throw new InvalidOperationException($"Missing Version for {elementName} entry.");
            }

            if (!UpdateVersion.TryParse(versionString, out UpdateVersion parsedVersion)
                || parsedVersion.Major < 0
                || parsedVersion.Minor < 0
                || parsedVersion.Patch < 0
                || parsedVersion.Build < 0) {
                throw new InvalidOperationException($"Invalid Version for {elementName} entry: {versionString}");
            }

            if (string.IsNullOrWhiteSpace(path)) {
                throw new InvalidOperationException($"Missing Path for {elementName} entry.");
            }

            string normalizedCrc32 = NormalizeCrc32Hex(crc32);
            if (!Regex.IsMatch(normalizedCrc32, "^[0-9a-f]{8}$", RegexOptions.CultureInvariant)) {
                throw new InvalidOperationException($"Invalid crc32 for {elementName} entry.");
            }

            if (!long.TryParse(fileSizeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileSize)
                || fileSize <= 0
                || fileSize > UpdateSecurityPolicy.MaxPackageBytes) {
                throw new InvalidOperationException($"Invalid Filesize for {elementName} entry.");
            }

            Uri downloadUri = UpdateSecurityPolicy.NormalizePackageUri(path);

            return new UpdateInfo(
                channel,
                versionString.Trim(),
                parsedVersion,
                downloadUri,
                normalizedCrc32,
                fileSize);
        }

        private static string NormalizeCrc32Hex(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                trimmed = trimmed.Substring(2);
            }

            return trimmed.ToLowerInvariant();
        }
    }
}
