﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EbookReader.Exceptions.Epub;
using EbookReader.Helpers;
using EbookReader.Model;
using HtmlAgilityPack;
using ICSharpCode.SharpZipLib.Zip;
using PCLStorage;

namespace EbookReader.Service {
    public class EpubLoader {

        private FileService _fileService;

        public EpubLoader() {
            _fileService = new FileService();
        }

        public async Task<Model.Epub> GetEpub(string filename, byte[] filedata) {
            var folder = await this.LoadEpub(filename, filedata);

            var epubFolder = await FileSystem.Current.LocalStorage.GetFolderAsync(folder);

            var contentFilePath = await this.GetContentFilePath(epubFolder);

            var contentFileData = await _fileService.ReadFileData(contentFilePath, epubFolder);

            var xml = XDocument.Parse(contentFileData);

            var package = xml.Root;

            var epubVersion = this.GetEpubVersion(package);

            var epubParser = this.GetParserInstance(epubVersion, package);

            var epub = new Model.Epub() {
                Version = epubVersion,
                Title = epubParser.GetTitle(),
                Author = epubParser.GetAuthor(),
                Description = epubParser.GetDescription(),
                Language = epubParser.GetLanguage(),
                Spines = epubParser.GetSpines(),
                Files = epubParser.GetFiles(),
                Folder = folder,
            };

            return epub;
        }

        public async Task<string> GetChapter(Model.Epub epub, EpubSpine chapter) {
            var filename = epub.Files.Where(o => o.Id == chapter.Idref).First();
            var folder = await FileSystem.Current.LocalStorage.GetFolderAsync(epub.Folder);
            return await _fileService.ReadFileData(string.Format("OEBPS/{0}", filename.Href), folder);
        }

        public bool HasNextChapter(Model.Epub epub, EpubSpine currentChapter) {
            var indexOf = epub.Spines.ToList().IndexOf(currentChapter);
            return epub.Spines.Count() > indexOf;
        }

        public async Task<Model.EpubLoader.HtmlResult> PrepareHTML(string html, Model.Epub epub) {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var images = await this.PrepareHtmlImages(doc, epub);

            var result = new Model.EpubLoader.HtmlResult {
                Html = doc.DocumentNode.Descendants("body").First().InnerHtml,
                Images = images,
            };

            return result;
        }

        private async Task<List<Model.EpubLoader.Image>> PrepareHtmlImages(HtmlDocument doc, Model.Epub epub) {
            var images = doc.DocumentNode.Descendants("img").ToList();
            var imagesModel = new List<Model.EpubLoader.Image>();

            //TODO[bares]: nacitat kazdy soubor pouze jednou
            var cnt = 1;
            foreach (var image in images) {
                var srcAttribute = image.Attributes.FirstOrDefault(o => o.Name == "src");

                if (srcAttribute != null) {

                    imagesModel.Add(new Model.EpubLoader.Image {
                        ID = cnt,
                        FileName = srcAttribute.Value,
                    });

                    image.Attributes.Add(doc.CreateAttribute("data-js-ebook-image-id", cnt.ToString()));

                    cnt++;
                }
            }

            var epubFolder = await FileSystem.Current.LocalStorage.GetFolderAsync(epub.Folder);

            foreach (var imageModel in imagesModel) {
                var extension = imageModel.FileName.Split('.').Last();

                var fileName = string.Format("OEBPS/{0}", imageModel.FileName.Replace("../", "")).Replace("//", "/");

                var file = await _fileService.OpenFile(fileName, epubFolder);

                using (var stream = await file.OpenAsync(FileAccess.Read)) {
                    var base64 = Base64Helper.GetFileBase64(stream);

                    imageModel.Data = string.Format("data:image/{0};base64,{1}", extension, base64);
                }

            }

            return imagesModel;

        }

        private Epub.EpubParser GetParserInstance(EpubVersion version, XElement package) {
            switch (version) {
                case EpubVersion.V200:
                    return new Epub.Epub200Parser(package);
                case EpubVersion.V300:
                    return new Epub.Epub300Parser(package);
                case EpubVersion.V301:
                    return new Epub.Epub301Parser(package);
            }

            throw new UnknownEpubVersionException();
        }

        private EpubVersion GetEpubVersion(XElement package) {
            var version = package.Attributes().First(o => o.Name.LocalName == "version").Value;
            return EpubVersionHelper.ParseVersion(version);
        }

        private async Task<string> GetContentFilePath(IFolder epubFolder) {
            var containerFile = await _fileService.OpenFile("META-INF/container.xml", epubFolder);
            var containerFileContent = await containerFile.ReadAllTextAsync();
            var xmlContainer = XDocument.Parse(containerFileContent);
            var contentFilePath = xmlContainer.Root
                .Descendants()
                .First(o => o.Name.LocalName == "rootfiles")
                .Descendants()
                .First(o => o.Name.LocalName == "rootfile")
                .Attributes()
                .First(o => o.Name.LocalName == "full-path")
                .Value;
            return contentFilePath;
        }

        private async Task<string> LoadEpub(string filename, byte[] filedata) {
            var folderName = filename.Split('.').First();

            var rootFolder = FileSystem.Current.LocalStorage;
            var folder = await rootFolder.CreateFolderAsync(folderName, CreationCollisionOption.ReplaceExisting);
            var file = await folder.CreateFileAsync("temp.zip", CreationCollisionOption.OpenIfExists);

            using (Stream stream = await file.OpenAsync(FileAccess.ReadAndWrite)) {
                await stream.WriteAsync(filedata, 0, filedata.Length);
                using (var zf = new ZipFile(stream)) {
                    foreach (ZipEntry zipEntry in zf) {

                        if (zipEntry.IsFile) {
                            var zipEntryStream = zf.GetInputStream(zipEntry);

                            var name = _fileService.GetLocalFileName(zipEntry.Name);

                            var fileFolder = await _fileService.GetFileFolder(zipEntry.Name, folder);

                            IFile zipEntryFile = await fileFolder.CreateFileAsync(name, CreationCollisionOption.OpenIfExists);
                            var str = zf.GetInputStream(zipEntry);
                            using (Stream outPutFileStream = await zipEntryFile.OpenAsync(FileAccess.ReadAndWrite)) {
                                await str.CopyToAsync(outPutFileStream);
                            }
                        }
                    }
                }
            }

            return folder.Name;
        }
    }
}
