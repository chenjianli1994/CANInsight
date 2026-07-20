// 稳健的OpenXML PPT报告生成器：单页模板克隆 + 追加累积 + 占位符/图片/表格填充
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace PCAN_Client.ReportAuto
{
    /// <summary>
    /// PPT报告构建器：基于单页空白模板，按“分析项目类型”逐页克隆填充并追加累积。
    /// 第1次AppendPage填充模板页本身；之后每次克隆模板页追加为新页再填充。
    /// </summary>
    internal class PptReportBuilder : IDisposable
    {
        private string _templatePath;       // 原始单页模板(只读,克隆源)
        private PresentationDocument _tplDoc;
        private string _currentPath;        // 当前累积报告(读写)
        private PresentationDocument _reportDoc;
        private bool _firstPageFilled = false;
        private bool _disposed = false;

        public int PageCount
        {
            get
            {
                if (_reportDoc == null) return 0;
                try { return _reportDoc.PresentationPart.SlideParts.Count(); }
                catch { return 0; }
            }
        }

        /// <summary>开始新报告：复制模板为当前报告</summary>
        public void StartReport(string templatePath)
        {
            Close();
            _templatePath = templatePath;
            _currentPath = Path.Combine(Path.GetTempPath(),
                "ReportAuto_" + Guid.NewGuid().ToString("N") + ".pptx");
            File.Copy(templatePath, _currentPath, true);
            System.Diagnostics.Debug.WriteLine($"[PptReportBuilder] 复制模板: {templatePath}");
            System.Diagnostics.Debug.WriteLine($"[PptReportBuilder] 工作副本: {_currentPath}");
            _tplDoc = PresentationDocument.Open(_templatePath, false);
            _reportDoc = PresentationDocument.Open(_currentPath, true);
            _firstPageFilled = false;
        }

        /// <summary>
        /// 追加一页：第1次填充模板页,之后克隆模板页追加。images=Source→Bitmap(如"chart"),
        /// values=占位符KEY→值字符串(替换模板里的 {{KEY}})
        /// </summary>
        public void AppendPage(AnalysisType type,
            Dictionary<string, Bitmap> images, Dictionary<string, string> values)
        {
            if (_reportDoc == null)
                throw new InvalidOperationException("报告未开始,请先调用StartReport");
            SlidePart target;
            if (!_firstPageFilled)
            {
                target = GetFirstSlidePart(_reportDoc);
                _firstPageFilled = true;
            }
            else
            {
                target = CloneTemplateSlideTo(_reportDoc);
            }
            FillSlide(target, type, images, values);
            _reportDoc.PresentationPart.Presentation.Save();
        }

        /// <summary>另存当前报告到指定路径</summary>
        public void SaveAs(string outPath)
        {
            if (_reportDoc != null)
            {
                _reportDoc.PresentationPart.Presentation.Save();
                _reportDoc.Dispose();
                _reportDoc = null;
            }
            File.Copy(_currentPath, outPath, true);
            _reportDoc = PresentationDocument.Open(_currentPath, true);
        }

        /// <summary>取文档SlideIdList中第一个显示的slide(避免取到孤儿slide part)</summary>
        private static SlidePart GetFirstSlidePart(PresentationDocument doc)
        {
            var presPart = doc.PresentationPart;
            var firstId = presPart.Presentation.SlideIdList?.ChildElements.OfType<SlideId>().FirstOrDefault();
            if (firstId != null && firstId.RelationshipId != null)
            {
                var sp = presPart.GetPartById(firstId.RelationshipId.Value) as SlidePart;
                if (sp != null) return sp;
            }
            return presPart.SlideParts.First();
        }

        /// <summary>从模板克隆一页追加到报告(复用报告已有layout,复制图片关系并重映射rId)</summary>
        private SlidePart CloneTemplateSlideTo(PresentationDocument reportDoc)
        {
            var presPart = reportDoc.PresentationPart;
            var tplSlide = GetFirstSlidePart(_tplDoc);
            var newSlide = presPart.AddNewPart<SlidePart>();
            // 复制slide XML(深拷贝,不含part关系)
            newSlide.Slide = (Slide)tplSlide.Slide.Clone();

            // 复用报告已有layout(第一个slide的SlideLayoutPart)
            var firstSlide = presPart.SlideParts.First(s => s != newSlide);
            if (firstSlide.SlideLayoutPart != null)
            {
                newSlide.AddPart(firstSlide.SlideLayoutPart, "rId1");
            }

            // 复制图片关系并建立 旧rId→新rId 映射
            var ridMap = new Dictionary<string, string>();
            foreach (var pair in tplSlide.Parts)
            {
                if (pair.OpenXmlPart is ImagePart srcImg)
                {
                    string newRid;
                    ImagePart newImg;
                    try
                    {
                        newImg = newSlide.AddImagePart(srcImg.ContentType);
                        using (var s = srcImg.GetStream(FileMode.Open))
                        using (var d = newImg.GetStream(FileMode.Create))
                            s.CopyTo(d);
                        newRid = newSlide.GetIdOfPart(newImg);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("克隆图片关系失败: " + ex.Message);
                        continue;
                    }
                    ridMap[pair.RelationshipId] = newRid;
                }
            }
            // 重映射 blip r:embed
            foreach (var blip in newSlide.Slide.Descendants<A.Blip>())
            {
                if (blip.Embed != null && !string.IsNullOrEmpty(blip.Embed.Value)
                    && ridMap.ContainsKey(blip.Embed.Value))
                {
                    blip.Embed.Value = ridMap[blip.Embed.Value];
                }
            }

            // 加入 SlideIdList
            var pres = presPart.Presentation;
            if (pres.SlideIdList == null)
                pres.SlideIdList = new SlideIdList();
            uint maxId = 256;
            foreach (SlideId sid in pres.SlideIdList.ChildElements.OfType<SlideId>())
            {
                if (sid.Id != null && sid.Id.Value > maxId) maxId = sid.Id.Value;
            }
            var newSldId = new SlideId { Id = ++maxId };
            newSldId.RelationshipId = presPart.GetIdOfPart(newSlide);
            pres.SlideIdList.Append(newSldId);
            return newSlide;
        }

        /// <summary>填充一页:图片替换 + 占位符文本替换 + 表格写入</summary>
        private void FillSlide(SlidePart slidePart, AnalysisType type,
            Dictionary<string, Bitmap> images, Dictionary<string, string> values)
        {
            var slide = slidePart.Slide;

            // 1) 图片:按Shape名定位Picture,替换其ImagePart字节。
            //    先按 type.ImageShapes 配置匹配;配置为空或未命中时,按固定标识符兜底填chart截图。
            bool anyImageFilled = false;
            foreach (var pic in slide.Descendants<Picture>())
            {
                string name = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
                var item = (type.ImageShapes != null)
                    ? type.ImageShapes.FirstOrDefault(i => i.ShapeName == name)
                    : null;
                // 兜底:配置未命中但该图片是固定图表占位框,按chart填充
                if (item == null && name == TemplateShapeIds.ChartImage && images != null && images.ContainsKey("chart"))
                    item = new ImageShapeItem { ShapeName = name, Source = "chart" };
                if (item == null) continue;
                if (images == null || !images.TryGetValue(item.Source, out var bmp) || bmp == null) continue;
                if (ReplacePictureImage(slidePart, pic, bmp)) anyImageFilled = true;
            }
            // 旧JSON用中文名、模板改名后一个都匹配不到时,按固定标识符兜底
            if (!anyImageFilled && images != null && images.TryGetValue("chart", out var chartBmp) && chartBmp != null)
            {
                foreach (var pic in slide.Descendants<Picture>())
                {
                    string name = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
                    if (name != TemplateShapeIds.ChartImage) continue;
                    ReplacePictureImage(slidePart, pic, chartBmp);
                    break;
                }
            }

            // 2) 占位符文本:逐段落合并替换 {{KEY}}→值(支持跨run)
            foreach (var sp in slide.Descendants<Shape>())
            {
                ReplacePlaceholdersInTextBody(sp.TextBody, values);
            }
            // 表格内文本也做占位符替换
            foreach (var gf in slide.Descendants<GraphicFrame>())
            {
                var tbl = gf.Graphic?.GraphicData?.OfType<A.Table>().FirstOrDefault();
                if (tbl != null)
                {
                    foreach (var row in tbl.Elements<A.TableRow>())
                        foreach (var cell in row.OfType<A.TableCell>())
                            ReplacePlaceholdersInTextBody(cell.TextBody, values);
                }
            }

            // 3) 条件表:按Shape名定位,写指定行的单元格(单元格可含{{KEY}})
            if (type.Table != null && !string.IsNullOrEmpty(type.Table.ShapeName))
            {
                foreach (var gf in slide.Descendants<GraphicFrame>())
                {
                    string name = gf.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Name?.Value;
                    if (name != type.Table.ShapeName) continue;
                    var tbl = gf.Graphic?.GraphicData?.OfType<A.Table>().FirstOrDefault();
                    if (tbl == null) continue;
                    var rows = tbl.Elements<A.TableRow>();
                    if (type.Table.Row < 0 || type.Table.Row >= rows.Count()) break;
                    var cells = rows.ElementAt(type.Table.Row).OfType<A.TableCell>().ToList();
                    for (int ci = 0; ci < cells.Count && ci < type.Table.Cells.Count; ci++)
                    {
                        string val = type.Table.Cells[ci];
                        if (values != null)
                        {
                            foreach (var kv in values)
                                val = val.Replace("{{" + kv.Key + "}}", kv.Value);
                        }
                        var firstText = cells[ci].TextBody?.Descendants<A.Text>().FirstOrDefault();
                        if (firstText != null) firstText.Text = val;
                    }
                    break;
                }
            }

            // 4) 文字模板填充:将 __TEXT_TEMPLATE__ 内容写入文字Shape。
            //    优先按 type.TextShapeName 匹配;为空或未命中时按固定标识符兜底。
            if (values != null && values.ContainsKey("__TEXT_TEMPLATE__"))
            {
                string text = values["__TEXT_TEMPLATE__"];
                string targetName = !string.IsNullOrEmpty(type.TextShapeName)
                    ? type.TextShapeName : TemplateShapeIds.TextDesc;
                Shape target = null;
                // 第一轮:按目标名匹配
                foreach (var sp in slide.Descendants<Shape>())
                {
                    string name = sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value;
                    if (name == targetName) { target = sp; break; }
                }
                // 第二轮兜底:目标名未命中且非固定标识符,按固定标识符再找
                if (target == null && targetName != TemplateShapeIds.TextDesc)
                {
                    foreach (var sp in slide.Descendants<Shape>())
                    {
                        string name = sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (name == TemplateShapeIds.TextDesc) { target = sp; break; }
                    }
                }
                if (target != null)
                {
                    FillTextShape(target, text);
                }
                else
                {
                    Debug.WriteLine("[PptReportBuilder] 未找到文字模板Shape: " + targetName
                        + " (兜底 " + TemplateShapeIds.TextDesc + " 也未找到)");
                }
            }
        }

        /// <summary>辅助方法:把文字写入指定Shape(按换行拆段,自动识别标题和正文格式)</summary>
        void FillTextShape(Shape sp, string text)
        {
            var txBody = sp.TextBody;
            if (txBody == null) return;
            // 按换行拆分文字
            var lines = text.Split('\n');
            // 获取第一个段落作为模板（保留其格式属性）
            var firstPara = txBody.Elements<A.Paragraph>().FirstOrDefault();
            if (firstPara == null) return;
            // 先清空所有段落
            var existingParas = txBody.Elements<A.Paragraph>().ToList();
            foreach (var p in existingParas) p.Remove();
            // 为每行创建新段落（克隆第一个段落以保留格式）
            foreach (string line in lines)
            {
                var newPara = (A.Paragraph)firstPara.CloneNode(true);
                string trimmedLine = line.TrimEnd('\r');
                bool isTitle = IsTitleLine(trimmedLine);
                
                // 设置文字到第一个run
                var firstRun = newPara.Descendants<A.Run>().FirstOrDefault();
                if (firstRun != null)
                {
                    var textElement = firstRun.GetFirstChild<A.Text>();
                    if (textElement != null)
                        textElement.Text = trimmedLine;
                    // 清除后续run的文字（保留格式）
                    foreach (var r in newPara.Descendants<A.Run>().Skip(1))
                    {
                        var t = r.GetFirstChild<A.Text>();
                        if (t != null)
                            t.Text = "";
                    }
                    // 应用标题或正文格式
                    ApplyRunFormat(firstRun, isTitle);
                }
                else
                {
                    // 没有run，创建一个
                    var run = new A.Run();
                    run.RunProperties = new A.RunProperties { Language = "zh-CN" };
                    run.Text = new A.Text { Text = trimmedLine };
                    newPara.Append(run);
                    ApplyRunFormat(run, isTitle);
                }
                txBody.Append(newPara);
            }
        }

        /// <summary>判断是否为标题行(以中文数字+顿号开头,如"一、"、"二、")</summary>
        bool IsTitleLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(line, @"^[一二三四五六七八九十]+、");
        }

        /// <summary>为Run应用标题或正文格式</summary>
        void ApplyRunFormat(A.Run run, bool isTitle)
        {
            var rPr = run.RunProperties;
            if (rPr == null)
            {
                rPr = new A.RunProperties { Language = "zh-CN" };
                run.RunProperties = rPr;
            }
            
            // 设置字体大小(10pt = 1000百分之一磅)
            rPr.FontSize = 1000;
            
            // 设置粗体
            rPr.Bold = isTitle ? true : false;
            
            // 移除现有的字体设置
            var existingLatin = rPr.GetFirstChild<A.LatinFont>();
            if (existingLatin != null) existingLatin.Remove();
            var existingEa = rPr.GetFirstChild<A.EastAsianFont>();
            if (existingEa != null) existingEa.Remove();
            
            // 添加字体设置为黑体
            rPr.AppendChild(new A.LatinFont { Typeface = "黑体" });
            rPr.AppendChild(new A.EastAsianFont { Typeface = "黑体" });
            
            // 移除现有颜色设置
            var existingSolidFill = rPr.GetFirstChild<A.SolidFill>();
            if (existingSolidFill != null) existingSolidFill.Remove();
            
            // 设置颜色
            if (isTitle)
            {
                // 标题：暗板岩蓝着色1浅色40% (使用固定RGB色值)
                var solidFill = new A.SolidFill();
                solidFill.RgbColorModelHex = new A.RgbColorModelHex 
                { 
                    Val = "5B9BD5"  // 浅蓝色
                };
                rPr.AppendChild(solidFill);
            }
            else
            {
                // 正文：黑色
                var solidFill = new A.SolidFill 
                { 
                    RgbColorModelHex = new A.RgbColorModelHex { Val = "000000" } 
                };
                rPr.AppendChild(solidFill);
            }
        }

        /// <summary>辅助方法:替换Picture的ImagePart字节为指定PNG,成功返回true</summary>
        bool ReplacePictureImage(SlidePart sp, Picture pic, Bitmap bmp)
        {
            var blip = pic.BlipFill?.Blip;
            if (blip == null || blip.Embed == null || string.IsNullOrEmpty(blip.Embed.Value)) return false;
            var imgPart = sp.GetPartById(blip.Embed.Value) as ImagePart;
            if (imgPart == null) return false;
            try
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    using (var dst = imgPart.GetStream(FileMode.Create))
                        ms.CopyTo(dst);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("替换图片失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>段落级占位符替换:合并段落所有run文本,替换{{KEY}},写回首个run,清空其余run</summary>
        private void ReplacePlaceholdersInTextBody(OpenXmlElement txBody, Dictionary<string, string> values)
        {
            if (txBody == null || values == null || values.Count == 0) return;
            foreach (var para in txBody.Descendants<A.Paragraph>())
            {
                var texts = para.Descendants<A.Text>().ToList();
                if (texts.Count == 0) continue;
                string combined = string.Concat(texts.Select(t => t.Text));
                string replaced = combined;
                foreach (var kv in values)
                    replaced = replaced.Replace("{{" + kv.Key + "}}", kv.Value);
                if (replaced == combined) continue; // 未变化
                texts[0].Text = replaced;
                for (int i = 1; i < texts.Count; i++) texts[i].Text = "";
            }
        }

        public void Close()
        {
            if (_tplDoc != null) { _tplDoc.Dispose(); _tplDoc = null; }
            if (_reportDoc != null) { _reportDoc.Dispose(); _reportDoc = null; }
            // 清理临时文件
            if (!string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
            {
                try { File.Delete(_currentPath); } catch { }
            }
            _currentPath = null;
            _templatePath = null;
            _firstPageFilled = false;
        }

        public void Dispose() { Close(); _disposed = true; }
    }
}
