using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SistecHub.Modulos.AcessoRemoto;

public class RemoteViewerFileItem
{
    private static SolidColorBrush CreateBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static readonly SolidColorBrush BrushFolderStroke = CreateBrush("#D97706");
    private static readonly SolidColorBrush BrushFolderFill = CreateBrush("#FEF3C7");

    private static readonly SolidColorBrush BrushExeStroke = CreateBrush("#4F46E5");
    private static readonly SolidColorBrush BrushExeFill = CreateBrush("#EEF2FF");

    private static readonly SolidColorBrush BrushMsiStroke = CreateBrush("#7C3AED");
    private static readonly SolidColorBrush BrushMsiFill = CreateBrush("#EDE9FE");

    private static readonly SolidColorBrush BrushScriptStroke = CreateBrush("#0284C7");
    private static readonly SolidColorBrush BrushScriptFill = CreateBrush("#E0F2FE");

    private static readonly SolidColorBrush BrushJavaStroke = CreateBrush("#DC2626");
    private static readonly SolidColorBrush BrushJavaFill = CreateBrush("#FEE2E2");

    private static readonly SolidColorBrush BrushZipStroke = CreateBrush("#EA580C");
    private static readonly SolidColorBrush BrushZipFill = CreateBrush("#FFEDD5");

    private static readonly SolidColorBrush BrushImageStroke = CreateBrush("#059669");
    private static readonly SolidColorBrush BrushImageFill = CreateBrush("#D1FAE5");

    private static readonly SolidColorBrush BrushTextStroke = CreateBrush("#475569");
    private static readonly SolidColorBrush BrushTextFill = CreateBrush("#F1F5F9");

    private static readonly SolidColorBrush BrushIsoStroke = CreateBrush("#8B5CF6");
    private static readonly SolidColorBrush BrushIsoFill = CreateBrush("#EDE9FE");

    private static readonly SolidColorBrush BrushRdpStroke = CreateBrush("#0284C7");
    private static readonly SolidColorBrush BrushRdpFill = CreateBrush("#E0F2FE");

    private static readonly SolidColorBrush BrushPdfStroke = CreateBrush("#DC2626");
    private static readonly SolidColorBrush BrushPdfFill = CreateBrush("#FEE2E2");

    private static readonly SolidColorBrush BrushHtmlStroke = CreateBrush("#EA580C");
    private static readonly SolidColorBrush BrushHtmlFill = CreateBrush("#FFEDD5");

    private static readonly SolidColorBrush BrushWordStroke = CreateBrush("#2563EB");
    private static readonly SolidColorBrush BrushWordFill = CreateBrush("#DBEAFE");

    private static readonly SolidColorBrush BrushExcelStroke = CreateBrush("#16A34A");
    private static readonly SolidColorBrush BrushExcelFill = CreateBrush("#DCFCE7");

    private static readonly SolidColorBrush BrushPptStroke = CreateBrush("#D97706");
    private static readonly SolidColorBrush BrushPptFill = CreateBrush("#FEF3C7");

    private static readonly SolidColorBrush BrushAccessStroke = CreateBrush("#9333EA");
    private static readonly SolidColorBrush BrushAccessFill = CreateBrush("#F3E8FF");

    private static readonly SolidColorBrush BrushMediaStroke = CreateBrush("#EC4899");
    private static readonly SolidColorBrush BrushMediaFill = CreateBrush("#FCE7F3");

    private static readonly SolidColorBrush BrushSysStroke = CreateBrush("#0891B2");
    private static readonly SolidColorBrush BrushSysFill = CreateBrush("#CFFAFE");

    private static readonly SolidColorBrush BrushCodeStroke = CreateBrush("#6366F1");
    private static readonly SolidColorBrush BrushCodeFill = CreateBrush("#EEF2FF");

    private static readonly SolidColorBrush BrushDefaultStroke = CreateBrush("#64748B");
    private static readonly SolidColorBrush BrushDefaultFill = CreateBrush("#F1F5F9");

    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public string ModTime { get; set; } = "";

    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public string TypeStr
    {
        get
        {
            if (IsDir) return "Pasta de arquivos";
            return Extension switch
            {
                ".exe" => "Aplicativo (.exe)",
                ".msi" => "Instalador Windows (.msi)",
                ".bat" or ".cmd" => "Arquivo de Lote (.bat)",
                ".ps1" => "Script PowerShell (.ps1)",
                ".vbs" => "Script VBScript (.vbs)",
                ".sh" or ".bash" => "Script Shell (.sh)",
                ".jar" => "Executável Java (.jar)",
                ".rar" => "Arquivo Compactado (.rar)",
                ".zip" => "Arquivo Compactado (.zip)",
                ".7z" => "Arquivo Compactado (.7z)",
                ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" => "Arquivo Compactado (.tar.gz)",
                ".cab" => "Arquivo de Gabinete (.cab)",
                ".png" => "Imagem PNG (.png)",
                ".jpg" or ".jpeg" => "Imagem JPEG (.jpg)",
                ".gif" => "Imagem GIF (.gif)",
                ".bmp" => "Imagem BMP (.bmp)",
                ".ico" => "Arquivo de Ícone (.ico)",
                ".webp" => "Imagem WebP (.webp)",
                ".svg" => "Vetor SVG (.svg)",
                ".txt" => "Documento de Texto (.txt)",
                ".log" => "Arquivo de Log (.log)",
                ".md" => "Documento Markdown (.md)",
                ".rtf" => "Texto em Rich Text (.rtf)",
                ".iso" => "Imagem de Disco (.iso)",
                ".img" => "Arquivo de Imagem (.img)",
                ".vhd" or ".vhdx" => "Disco Rígido Virtual (.vhd)",
                ".dmg" => "Imagem de Disco Apple (.dmg)",
                ".rdp" => "Conexão de Área de Trabalho Remota (.rdp)",
                ".pdf" => "Documento PDF (.pdf)",
                ".html" or ".htm" or ".htmk" or ".xhtml" => "Documento HTML (.html)",
                ".url" => "Atalho da Internet (.url)",
                ".doc" or ".docx" or ".dot" or ".dotx" or ".docm" => "Documento Microsoft Word (.docx)",
                ".odt" => "Texto OpenDocument (.odt)",
                ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".xltx" => "Planilha Microsoft Excel (.xlsx)",
                ".csv" => "Arquivo de Planilha (.csv)",
                ".ods" => "Planilha OpenDocument (.ods)",
                ".ppt" or ".pptx" or ".pps" or ".ppsx" or ".pptm" => "Apresentação PowerPoint (.pptx)",
                ".odp" => "Apresentação OpenDocument (.odp)",
                ".accdb" or ".mdb" => "Banco de Dados Access (.accdb)",
                ".msg" or ".eml" => "Mensagem de E-mail (.msg)",
                ".pst" or ".ost" => "Arquivo de Dados Outlook (.pst)",
                ".json" => "Arquivo JSON (.json)",
                ".xml" => "Arquivo XML (.xml)",
                ".sql" => "Script de Banco SQL (.sql)",
                ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".php" or ".java" => "Arquivo de Código-Fonte",
                ".reg" => "Arquivo de Registro (.reg)",
                ".dll" => "Extensão de Aplicativo (.dll)",
                ".sys" => "Arquivo de Sistema (.sys)",
                ".cer" or ".crt" or ".pfx" => "Certificado Digital (.cer)",
                ".mp3" or ".wav" or ".flac" or ".wma" => "Arquivo de Áudio (.audio)",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "Arquivo de Vídeo (.video)",
                ".ini" or ".cfg" or ".conf" or ".inf" => "Arquivo de Configuração",
                { Length: > 1 } ext => $"Arquivo {ext.TrimStart('.').ToUpperInvariant()}",
                _ => "Arquivo"
            };
        }
    }

    public string IconData
    {
        get
        {
            if (IsDir)
                return "M 3,7 A 2,2 0 0 1 5,5 H 10 L 12,7 H 19 A 2,2 0 0 1 21,9 V 17 A 2,2 0 0 1 19,19 H 5 A 2,2 0 0 1 3,17 Z";

            return Extension switch
            {
                // .exe: Janela de aplicativo com barra de título e play/cursor
                ".exe" => "M 3,4 H 21 A 2,2 0 0 1 23,6 V 18 A 2,2 0 0 1 21,20 H 3 A 2,2 0 0 1 1,18 V 6 A 2,2 0 0 1 3,4 Z M 1,9 H 23 M 5,6.5 H 5.01 M 8,6.5 H 8.01 M 11,6.5 H 11.01 M 10,12 L 14,14.5 L 10,17 Z",

                // .msi: Caixa 3D de pacote de instalação
                ".msi" => "M 12,2 L 21,7 V 17 L 12,22 L 3,17 V 7 Z M 3,7 L 12,12 L 21,7 M 12,12 V 22 M 7.5,4.5 L 16.5,9.5",

                // .bat / .cmd / .ps1: Prompt de terminal
                ".bat" or ".cmd" or ".ps1" or ".sh" or ".bash" or ".vbs" => "M 3,4 H 21 A 2,2 0 0 1 23,6 V 18 A 2,2 0 0 1 21,20 H 3 A 2,2 0 0 1 1,18 V 6 A 2,2 0 0 1 3,4 Z M 5,11 L 9,14 L 5,17 M 11,17 H 17",

                // .jar: Xícara Java / package
                ".jar" => "M 18,8 H 19 A 3,3 0 0 1 22,11 A 3,3 0 0 1 19,14 H 18 M 2,8 H 18 V 14 A 4,4 0 0 1 14,18 H 6 A 4,4 0 0 1 2,14 Z M 6,2 V 5 M 10,2 V 5 M 14,2 V 5",

                // .rar / .zip / .7z: Arquivo compactado com zíper
                ".rar" or ".zip" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".cab" => "M 4,4 H 20 A 2,2 0 0 1 22,6 V 18 A 2,2 0 0 1 20,20 H 4 A 2,2 0 0 1 2,18 V 6 A 2,2 0 0 1 4,4 Z M 12,4 V 11 M 10,6 H 14 M 10,8 H 14 M 11,11 H 13 V 14 H 11 Z",

                // .png / .jpg / .jpeg: Imagem fotográfica
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".svg" or ".tiff" or ".tif" => "M 19,3 H 5 A 2,2 0 0 0 3,5 V 19 A 2,2 0 0 0 5,21 H 19 A 2,2 0 0 0 21,19 V 5 A 2,2 0 0 0 19,3 Z M 8.5,10 A 1.5,1.5 0 1 1 8.5,7 A 1.5,1.5 0 0 1 8.5,10 Z M 21,15 L 16,10 L 5,21",

                // .txt / .log / .md / .rtf: Documento com linhas horizontais de texto
                ".txt" or ".log" or ".md" or ".rtf" or ".ini" or ".cfg" or ".conf" or ".inf" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8,12 H 16 M 8,15 H 16 M 8,18 H 13",

                // .iso / .img / .vhd: Imagem de disco óptica
                ".iso" or ".img" or ".vhd" or ".vhdx" or ".bin" or ".cue" or ".dmg" => "M 12,2 A 10,10 0 1 0 22,12 A 10,10 0 0 0 12,2 Z M 12,9.5 A 2.5,2.5 0 1 1 9.5,12 A 2.5,2.5 0 0 1 12,9.5 Z M 12,2 V 5 M 12,19 V 22 M 2,12 H 5 M 19,12 H 22",

                // .rdp: Conexão de desktop remoto com tela e setas
                ".rdp" => "M 2,3 H 22 V 16 H 2 Z M 8,20 H 16 M 12,16 V 20 M 6,8 L 8.5,10.5 L 6,13 M 18,8 L 15.5,10.5 L 18,13 M 10,10.5 H 14",

                // .pdf: Documento PDF com a marca P
                ".pdf" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8,11 H 11 A 1.5,1.5 0 0 1 12.5,12.5 A 1.5,1.5 0 0 1 11,14 H 8 V 17 M 8,14 H 11 M 15,11 V 17",

                // .html / .htm / .htmk / .xhtml / .url: Tags de código < / >
                ".html" or ".htm" or ".htmk" or ".xhtml" or ".url" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8.5,12.5 L 6.5,14.5 L 8.5,16.5 M 15.5,12.5 L 17.5,14.5 L 15.5,16.5 M 13,11.5 L 11,17.5",

                // .docx / .doc / .dot: Documento Word com "W"
                ".doc" or ".docx" or ".dot" or ".dotx" or ".docm" or ".odt" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 7.5,12 L 9,17.5 L 11,14 L 13,17.5 L 14.5,12",

                // .xlsx / .xls / .csv: Planilha Excel com grade
                ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".xltx" or ".csv" or ".ods" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 7,12 H 17 M 7,15.5 H 17 M 11.5,10 V 18",

                // .pptx / .ppt / .pps: Apresentação PowerPoint com "P"
                ".ppt" or ".pptx" or ".pps" or ".ppsx" or ".pptm" or ".odp" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8,11 H 12 A 2,2 0 0 1 14,13 A 2,2 0 0 1 12,15 H 8 V 17.5 M 8,15 H 12",

                // .accdb / .mdb: Banco de dados Access
                ".accdb" or ".mdb" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8,12 C 8,11 16,11 16,12 C 16,13 8,13 8,12 Z M 8,14.5 C 8,15.5 16,15.5 16,14.5 M 8,17 C 8,18 16,18 16,17",

                // .msg / .eml / .pst: Mensagens Outlook com envelope
                ".msg" or ".eml" or ".pst" or ".ost" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 7,12 L 12,15 L 17,12 M 7,17 H 17",

                // Código fonte e dados: JSON, XML, SQL, código
                ".json" or ".xml" or ".sql" or ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".php" or ".java" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 8.5,12.5 L 6.5,14.5 L 8.5,16.5 M 15.5,12.5 L 17.5,14.5 L 15.5,16.5 M 13,11.5 L 11,17.5",

                // Áudio e vídeo: Play
                ".mp3" or ".wav" or ".flac" or ".wma" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 10,12 L 15,14.5 L 10,17 Z",

                // Sistema e Registro
                ".reg" or ".dll" or ".sys" or ".cer" or ".crt" or ".pfx" => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20 M 12,11 A 3,3 0 1 0 12,17 A 3,3 0 1 0 12,11 Z M 12,9.5 V 11 M 12,17 V 18.5 M 9.5,14 H 11 M 17,14 H 18.5",

                // Padrão: Documento
                _ => "M 14,2 H 6 A 2,2 0 0 0 4,4 V 20 A 2,2 0 0 0 6,22 H 18 A 2,2 0 0 0 20,20 V 8 Z M 14,2 V 8 H 20"
            };
        }
    }

    public System.Windows.Media.Brush IconStroke
    {
        get
        {
            if (IsDir) return BrushFolderStroke;

            return Extension switch
            {
                ".exe" => BrushExeStroke,
                ".msi" => BrushMsiStroke,
                ".bat" or ".cmd" or ".ps1" or ".sh" or ".bash" or ".vbs" => BrushScriptStroke,
                ".jar" => BrushJavaStroke,
                ".rar" or ".zip" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".cab" => BrushZipStroke,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".svg" or ".tiff" or ".tif" => BrushImageStroke,
                ".txt" or ".log" or ".md" or ".rtf" or ".ini" or ".cfg" or ".conf" or ".inf" => BrushTextStroke,
                ".iso" or ".img" or ".vhd" or ".vhdx" or ".bin" or ".cue" or ".dmg" => BrushIsoStroke,
                ".rdp" => BrushRdpStroke,
                ".pdf" => BrushPdfStroke,
                ".html" or ".htm" or ".htmk" or ".xhtml" or ".url" => BrushHtmlStroke,
                ".doc" or ".docx" or ".dot" or ".dotx" or ".docm" or ".odt" => BrushWordStroke,
                ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".xltx" or ".csv" or ".ods" => BrushExcelStroke,
                ".ppt" or ".pptx" or ".pps" or ".ppsx" or ".pptm" or ".odp" => BrushPptStroke,
                ".accdb" or ".mdb" => BrushAccessStroke,
                ".msg" or ".eml" or ".pst" or ".ost" => BrushRdpStroke,
                ".json" or ".xml" or ".sql" or ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".php" or ".java" => BrushCodeStroke,
                ".mp3" or ".wav" or ".flac" or ".wma" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => BrushMediaStroke,
                ".reg" or ".dll" or ".sys" or ".cer" or ".crt" or ".pfx" => BrushSysStroke,
                _ => BrushDefaultStroke
            };
        }
    }

    public System.Windows.Media.Brush IconFill
    {
        get
        {
            if (IsDir) return BrushFolderFill;

            return Extension switch
            {
                ".exe" => BrushExeFill,
                ".msi" => BrushMsiFill,
                ".bat" or ".cmd" or ".ps1" or ".sh" or ".bash" or ".vbs" => BrushScriptFill,
                ".jar" => BrushJavaFill,
                ".rar" or ".zip" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".cab" => BrushZipFill,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".svg" or ".tiff" or ".tif" => BrushImageFill,
                ".txt" or ".log" or ".md" or ".rtf" or ".ini" or ".cfg" or ".conf" or ".inf" => BrushTextFill,
                ".iso" or ".img" or ".vhd" or ".vhdx" or ".bin" or ".cue" or ".dmg" => BrushIsoFill,
                ".rdp" => BrushRdpFill,
                ".pdf" => BrushPdfFill,
                ".html" or ".htm" or ".htmk" or ".xhtml" or ".url" => BrushHtmlFill,
                ".doc" or ".docx" or ".dot" or ".dotx" or ".docm" or ".odt" => BrushWordFill,
                ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".xltx" or ".csv" or ".ods" => BrushExcelFill,
                ".ppt" or ".pptx" or ".pps" or ".ppsx" or ".pptm" or ".odp" => BrushPptFill,
                ".accdb" or ".mdb" => BrushAccessFill,
                ".msg" or ".eml" or ".pst" or ".ost" => BrushRdpFill,
                ".json" or ".xml" or ".sql" or ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".php" or ".java" => BrushCodeFill,
                ".mp3" or ".wav" or ".flac" or ".wma" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => BrushMediaFill,
                ".reg" or ".dll" or ".sys" or ".cer" or ".crt" or ".pfx" => BrushSysFill,
                _ => BrushDefaultFill
            };
        }
    }

    public System.Windows.FontWeight NameFontWeight => IsDir ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;

    public string SizeStr
    {
        get
        {
            if (IsDir) return "-";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{(Size / 1024.0):F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{(Size / (1024.0 * 1024.0)):F1} MB";
            return $"{(Size / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
    }
}

public class RemoteViewerChatItem
{
    public string Sender { get; set; } = "";
    public string Time { get; set; } = "";
    public string Text { get; set; } = "";
    public string BgColor { get; set; } = "#F1F5F9";
    public string SenderColor { get; set; } = "#4F46E5";
}

public partial class AcessoRemotoViewerWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_ALTDOWN = 0x20;

    private const int VK_TAB = 0x09;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _clipboardListenerAdded;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private bool _remoteWinKeyDown;
    private bool _remoteAltKeyDown;
    private bool _remoteCtrlKeyDown;
    private bool _remoteShiftKeyDown;
    private bool _isTechnicianBlockedByClient;
    private readonly SemaphoreSlim _wsSendLock = new(1, 1);

    private List<AcessoRemotoClipboardFileItem> _viewerAdvertisedFiles = new();
    private readonly ConcurrentDictionary<string, RemoteClipboardPullStream> _viewerClipboardPullStreams = new();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (_hwnd != IntPtr.Zero)
            {
                int useImmersiveDarkMode = 1;
                if (DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
                }

                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(WndProc);
                _clipboardListenerAdded = AcessoRemotoClipboard.AddClipboardFormatListener(_hwnd);
            }

            InstallKeyboardHook();
        }
        catch
        {
            // Fallback seguro
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        UninstallKeyboardHook();

        if (_hwnd != IntPtr.Zero && _clipboardListenerAdded)
        {
            try { AcessoRemotoClipboard.RemoveClipboardFormatListener(_hwnd); } catch { }
            _clipboardListenerAdded = false;
        }

        if (_hwndSource != null)
        {
            try { _hwndSource.RemoveHook(WndProc); } catch { }
            _hwndSource = null;
        }

        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == AcessoRemotoClipboard.WM_CLIPBOARDUPDATE)
        {
            if (_isConnected && !AcessoRemotoClipboard.ShouldIgnoreClipboardChange())
            {
                OnLocalViewerClipboardChanged();
            }
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHookId != IntPtr.Zero)
            return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_keyboardHookId); } catch { }
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private bool ShouldRedirectSystemKeysToRemote()
    {
        if (!_isConnected || _hwnd == IntPtr.Zero || _isTechnicianBlockedByClient)
            return false;

        if (GetForegroundWindow() != _hwnd)
            return false;

        if (ViewScreen.Visibility != Visibility.Visible || WaitingCard.Visibility == Visibility.Visible)
            return false;

        if (ModalNewFolderOverlay.Visibility == Visibility.Visible)
            return false;

        if (TxtChatMessage.IsKeyboardFocused ||
            TxtCurrentPath.IsKeyboardFocused ||
            TxtServerUrl.IsKeyboardFocused ||
            TxtTargetId.IsKeyboardFocused ||
            TxtPassword.IsKeyboardFocused ||
            PwdWaitingPassword.IsKeyboardFocused ||
            TxtWaitingPasswordRevealed.IsKeyboardFocused ||
            TxtNewFolderName.IsKeyboardFocused)
        {
            return false;
        }

        return true;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ShouldRedirectSystemKeysToRemote())
        {
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kb.vkCode;
                bool altDown = (kb.flags & LLKHF_ALTDOWN) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                // Preserva o atalho global Ctrl + Alt + Espaço para abrir o SistecHub localmente
                if (vk == VK_SPACE && ctrlDown && altDown)
                {
                    return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                }

                bool isWinKey = vk == VK_LWIN || vk == VK_RWIN;
                bool isAltTab = vk == VK_TAB && (altDown || _remoteAltKeyDown);
                bool isAltEsc = vk == VK_ESCAPE && (altDown || _remoteAltKeyDown);
                bool isCtrlEsc = vk == VK_ESCAPE && (ctrlDown || _remoteCtrlKeyDown) && !altDown;
                bool isWinCombo = _remoteWinKeyDown ||
                                  (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                                  (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

                if (isWinKey || isAltTab || isAltEsc || isCtrlEsc || isWinCombo)
                {
                    if (isWinKey)
                        _remoteWinKeyDown = isKeyDown;

                    if (isAltTab && isKeyDown && !_remoteAltKeyDown)
                    {
                        _remoteAltKeyDown = true;
                        SendKeyPacket((ushort)VK_LMENU, isDown: true);
                    }

                    SendKeyPacket((ushort)vk, isDown: isKeyDown);
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void SendKeyPacket(ushort vk, bool isDown)
    {
        var packet = new byte[4];
        packet[0] = OpInput;
        packet[1] = isDown ? InputKeyDown : InputKeyUp;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), vk);
        SendInputPacket(packet);
    }

    private void ReleaseRemoteModifierKeys()
    {
        if (_remoteWinKeyDown)
        {
            _remoteWinKeyDown = false;
            SendKeyPacket(VK_LWIN, isDown: false);
            SendKeyPacket(VK_RWIN, isDown: false);
        }
        if (_remoteAltKeyDown)
        {
            _remoteAltKeyDown = false;
            SendKeyPacket(VK_LMENU, isDown: false);
            SendKeyPacket(VK_RMENU, isDown: false);
            SendKeyPacket(VK_MENU, isDown: false);
        }
        if (_remoteCtrlKeyDown)
        {
            _remoteCtrlKeyDown = false;
            SendKeyPacket(VK_LCONTROL, isDown: false);
            SendKeyPacket(VK_RCONTROL, isDown: false);
            SendKeyPacket(VK_CONTROL, isDown: false);
        }
        if (_remoteShiftKeyDown)
        {
            _remoteShiftKeyDown = false;
            SendKeyPacket(VK_LSHIFT, isDown: false);
            SendKeyPacket(VK_RSHIFT, isDown: false);
            SendKeyPacket(VK_SHIFT, isDown: false);
        }
    }

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _isConnected;
    private int _remoteWidth;
    private int _remoteHeight;
    private int _frameCount;
    private DateTime _lastFpsCheck = DateTime.UtcNow;
    private bool _isFullscreen;
    private WindowStyle _previousStyle = WindowStyle.SingleBorderWindow;
    private WindowState _previousState = WindowState.Normal;
    private ResizeMode _previousResizeMode = ResizeMode.CanResize;

    private bool _isInputBlocked;
    private int _unreadChatCount;
    private string _currentRemotePath = "C:\\";
    private FileStream? _downloadStream;
    private string _downloadDestPath = "";

    private const byte OpFrame = 0x01;
    private const byte OpInput = 0x02;
    private const byte OpCursor = 0x03;

    private bool _showRemoteCursor = true;
    private RemoteCursorState? _lastRemoteCursorState;
    private RemoteCursorType _currentTechnicianCursorType = RemoteCursorType.Arrow;

    private const byte InputMouseMove = 0x01;
    private const byte InputMouseDown = 0x02;
    private const byte InputMouseUp = 0x03;
    private const byte InputMouseWheel = 0x04;
    private const byte InputKeyDown = 0x05;
    private const byte InputKeyUp = 0x06;

    private readonly ObservableCollection<RemoteViewerFileItem> _fileItems = new();
    private readonly ObservableCollection<RemoteViewerChatItem> _chatItems = new();

    public AcessoRemotoViewerWindow(string? initialServerUrl = null, string? initialTargetId = null, string? initialPassword = null)
    {
        _keyboardHookProc = KeyboardHookCallback;
        InitializeComponent();

        LoadBrandLogo();
        RebuildMonitorButtons(Array.Empty<string>());

        LvFiles.ItemsSource = _fileItems;
        LstChat.ItemsSource = _chatItems;

        GlpiTechnicianSession.Initialize();
        UpdateWaitingGlpiAuthState();
        GlpiTechnicianSession.StateChanged += (_, _) => Dispatcher.Invoke(UpdateWaitingGlpiAuthState);

        TxtServerUrl.Text = initialServerUrl ?? AppSettingsStore.Load().RemotoServerUrl;
        TxtTargetId.Text = initialTargetId ?? "";
        TxtPassword.Password = initialPassword ?? "";

        if (!string.IsNullOrWhiteSpace(initialTargetId))
        {
            DisconnectedCard.Visibility = Visibility.Collapsed;
            Loaded += async (_, _) => await StartConnectAsync();
        }

        Activated += (_, _) =>
        {
            if (_isFullscreen)
                Topmost = true;
        };

        Deactivated += (_, _) =>
        {
            ReleaseRemoteModifierKeys();
            if (_isFullscreen)
                Topmost = false;
        };
    }

    private void LoadBrandLogo()
    {
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "sidebar-logo.png");
            if (!System.IO.File.Exists(logoPath)) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            ImgLogo.Source = bmp;
            ImgLogo.Visibility = Visibility.Visible;
            TxtBrandFallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Mantém o texto como alternativa.
        }
    }

    private void MenuQuickActions_Click(object sender, RoutedEventArgs e) => PopupActions.IsOpen = true;

    private void PopupActions_Click(object sender, RoutedEventArgs e) => PopupActions.IsOpen = false;

    #region Navegação por Abas (Segmented Pills)

    private void NavBtnScreen_Click(object sender, RoutedEventArgs e) => SwitchTab(0);
    private void NavBtnFiles_Click(object sender, RoutedEventArgs e) => SwitchTab(1);
    private void BtnChat_Click(object sender, RoutedEventArgs e) => SetChatOpen(ViewChat.Visibility != Visibility.Visible);

    private void BtnCloseChat_Click(object sender, RoutedEventArgs e) => SetChatOpen(false);

    private void SetChatOpen(bool open)
    {
        ViewChat.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        BtnChat.Background = open
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        BtnChat.BorderBrush = open
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
        PathChat.Stroke = open ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));

        if (open)
        {
            _unreadChatCount = 0;
            BadgeChat.Visibility = Visibility.Collapsed;
            if (_chatItems.Count > 0)
                LstChat.ScrollIntoView(_chatItems[_chatItems.Count - 1]);
            TxtChatMessage.Focus();
        }
        else if (ViewScreen.Visibility == Visibility.Visible)
        {
            ImgRemoteScreen.Focus();
        }
    }

    private void SwitchTab(int tabIndex)
    {
        NavBtnScreen.Background = tabIndex == 0 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1")) : Brushes.Transparent;
        NavBtnScreen.Foreground = tabIndex == 0 ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

        NavBtnFiles.Background = tabIndex == 1 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1")) : Brushes.Transparent;
        NavBtnFiles.Foreground = tabIndex == 1 ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));


        ViewScreen.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewFiles.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

        ToolbarBar.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (tabIndex == 0)
        {
            ImgRemoteScreen.Focus();
        }
    }

    #endregion

    #region Conexão & WebSocket

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        await StartConnectAsync();
    }

    private async Task StartConnectAsync()
    {
        var serverUrl = TxtServerUrl.Text.Trim();
        var targetId = TxtTargetId.Text.Trim().Replace(" ", "");
        var password = TxtPassword.Password.Trim();

        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(targetId))
        {
            MessageBox.Show("Por favor, preencha o Servidor e o ID Remoto.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ = AcessoRemotoLogService.SendLogAsync(targetId);

        try
        {
            SetConnectionState(connecting: true);
            LblStatusText.Text = "Conectando ao relay...";
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

            await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);

            var (techName, techAvatar) = GetEffectiveTechnicianInfo();

            var authPayload = JsonSerializer.Serialize(new
            {
                type = "connect_viewer",
                target_id = targetId,
                password = password,
                technician_name = techName,
                technician_avatar = techAvatar
            });

            var authBytes = Encoding.UTF8.GetBytes(authPayload);
            var ws = _ws;
            var cts = _cts;
            await ws.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true, cts.Token);

            _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao conectar: {ex.Message}", "Erro de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = EndSessionAndCloseAsync();
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[3 * 1024 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var totalReceived = 0;
                WebSocketReceiveResult result;

                do
                {
                    var segment = new ArraySegment<byte>(buffer, totalReceived, buffer.Length - totalReceived);
                    result = await ws.ReceiveAsync(segment, ct);
                    totalReceived += result.Count;
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, totalReceived);
                    ProcessControlMessage(json);
                }
                else if (result.MessageType == WebSocketMessageType.Binary && totalReceived > 0)
                {
                    ProcessBinaryPacket(buffer, totalReceived);
                }
            }
        }
        catch { }
        finally
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (!_isClosingSession && !_isReconnectingWithPassword && ReferenceEquals(_ws, ws))
                {
                    await EndSessionAndCloseAsync(
                        reason: "Conexão encerrada.",
                        displayMessageToTechnician: null);
                }
            });
        }
    }

    private void ProcessControlMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            var type = typeProp.GetString();
            switch (type)
            {
                case "viewer_connected":
                    Dispatcher.Invoke(() =>
                    {
                        _isConnected = true;
                        SetConnectionState(connected: true);
                        var isUnattended = root.TryGetProperty("unattended", out var uProp) && uProp.GetBoolean();
                        if (isUnattended)
                        {
                            _isReconnectingWithPassword = false;
                            _waitingAuthTcs?.TrySetResult((true, null));
                            LblStatusText.Text = "Conectado (Acesso não supervisionado)";
                            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                            WaitingCard.Visibility = Visibility.Collapsed;
                            ResetWaitingPasswordUiState(clearPassword: true);
                            ImgRemoteScreen.Focus();
                        }
                        else
                        {
                            LblStatusText.Text = "Aguardando autorização do usuário na máquina remota...";
                            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                            WaitingCard.Visibility = Visibility.Visible;
                            if (_isReconnectingWithPassword)
                            {
                                _isReconnectingWithPassword = false;
                                _waitingAuthTcs?.TrySetResult((false, "Senha incorreta ou acesso não supervisionado não configurado neste computador."));
                                ResetWaitingPasswordUiState(clearPassword: false);
                                ShowWaitingPasswordError("Senha incorreta ou acesso não supervisionado não configurado neste computador.");
                                FocusWaitingPasswordInput(selectAll: true);
                            }
                            else
                            {
                                ResetWaitingPasswordUiState(clearPassword: false);
                                FocusWaitingPasswordInput(selectAll: LblWaitingPasswordError.Visibility == Visibility.Visible);
                            }
                        }
                        DisconnectedCard.Visibility = Visibility.Collapsed;

                        SendJson(new { type = "fs_list", path = "C:\\" });

                        var (name, avatar) = GetEffectiveTechnicianInfo();
                        var initialPwd = !isUnattended ? TxtPassword.Password.Trim() : "";
                        SendJson(new
                        {
                            type = "technician_info",
                            technician_name = name,
                            technician_avatar = avatar,
                            password = string.IsNullOrEmpty(initialPwd) ? null : initialPwd
                        });
                    });
                    break;

                case AcessoRemotoProtocol.TypeAuthPasswordResult:
                    var authOk = root.TryGetProperty("success", out var aOkProp) && aOkProp.GetBoolean();
                    var authErr = root.TryGetProperty("message", out var aErrProp) ? aErrProp.GetString() : null;
                    bool hadWaitingTcs = _waitingAuthTcs?.TrySetResult((authOk, authErr)) == true;
                    Dispatcher.Invoke(() =>
                    {
                        if (authOk)
                        {
                            MarkSessionAccepted(unattended: true);
                        }
                        else if (!hadWaitingTcs && WaitingCard.Visibility == Visibility.Visible)
                        {
                            ResetWaitingPasswordUiState(clearPassword: false);
                            ShowWaitingPasswordError(string.IsNullOrWhiteSpace(authErr)
                                ? "Senha incorreta. Verifique a senha e tente novamente."
                                : authErr!);
                            FocusWaitingPasswordInput(selectAll: true);
                        }
                    });
                    break;

                case "monitors_list":
                    if (root.TryGetProperty("monitors", out var monProp) && monProp.ValueKind == JsonValueKind.Array)
                    {
                        var monNames = new List<string>();
                        foreach (var m in monProp.EnumerateArray())
                        {
                            var name = m.GetProperty("name").GetString() ?? "Monitor";
                            monNames.Add(name);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            MarkSessionAccepted();
                            RebuildMonitorButtons(monNames);
                        });
                    }
                    break;

                case "block_input_status":
                    var blocked = root.GetProperty("blocked").GetBoolean();
                    var success = root.GetProperty("success").GetBoolean();
                    Dispatcher.Invoke(() =>
                    {
                        if (success)
                        {
                            _isInputBlocked = blocked;
                            PathBlockInput.Data = System.Windows.Media.Geometry.Parse(
                                _isInputBlocked
                                    ? "M 7,11 V 7 A 5,5 0 0 1 17,7 M 5,11 H 19 A 2,2 0 0 1 21,13 V 20 A 2,2 0 0 1 19,22 H 5 A 2,2 0 0 1 3,20 V 13 A 2,2 0 0 1 5,11 Z"
                                    : "M 7,11 V 7 A 5,5 0 0 1 17,7 V 11 M 5,11 H 19 A 2,2 0 0 1 21,13 V 20 A 2,2 0 0 1 19,22 H 5 A 2,2 0 0 1 3,20 V 13 A 2,2 0 0 1 5,11 Z"
                            );
                            PathBlockInput.Stroke = _isInputBlocked ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                            BtnBlockInput.ToolTip = _isInputBlocked ? "Desbloquear entrada remota (atualmente bloqueada)" : "Bloquear teclado e mouse remotos";
                            BtnBlockInput.Background = _isInputBlocked
                                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))
                                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                            BtnBlockInput.BorderBrush = _isInputBlocked
                                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
                                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                        }
                        else
                        {
                            var errMsg = root.TryGetProperty("message", out var m) ? m.GetString() : "Permissão negada.";
                            MessageBox.Show($"Falha ao alterar bloqueio de entrada: {errMsg}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    });
                    break;

                case AcessoRemotoProtocol.TypeTechnicianInputBlocked:
                {
                    var techBlocked = root.TryGetProperty("blocked", out var tbProp) && tbProp.GetBoolean();
                    Dispatcher.Invoke(() => SetTechnicianInputBlockedByClient(techBlocked));
                    break;
                }

                case AcessoRemotoProtocol.TypeCursorUpdate:
                {
                    if (AcessoRemotoCursorHelper.TryParseMessage(root, out var cursorState))
                    {
                        Dispatcher.InvokeAsync(() => ApplyRemoteCursorState(cursorState));
                    }
                    break;
                }

                case "chat_message":
                    var senderName = root.TryGetProperty("sender", out var s) ? s.GetString() : "Remoto";
                    var text = root.TryGetProperty("text", out var t) ? t.GetString() : "";
                    if (AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(text))
                        break;

                    var time = root.TryGetProperty("time", out var tm) ? tm.GetString() : DateTime.Now.ToString("HH:mm:ss");

                    Dispatcher.Invoke(() =>
                    {
                        _chatItems.Add(new RemoteViewerChatItem
                        {
                            Sender = senderName ?? "Remoto",
                            Text = text ?? "",
                            Time = time ?? "",
                            BgColor = "#F1F5F9",
                            SenderColor = "#059669"
                        });

                        if (ViewChat.Visibility != Visibility.Visible)
                        {
                            _unreadChatCount++;
                            LblChatBadge.Text = _unreadChatCount > 99 ? "99+" : _unreadChatCount.ToString();
                            BadgeChat.Visibility = Visibility.Visible;
                        }
                        else if (_chatItems.Count > 0)
                        {
                            LstChat.ScrollIntoView(_chatItems[_chatItems.Count - 1]);
                        }
                    });
                    break;

                case AcessoRemotoProtocol.TypeClipboardText:
                    var clipText = root.TryGetProperty("text", out var ctProp) ? ctProp.GetString() : null;
                    if (!string.IsNullOrEmpty(clipText))
                    {
                        AcessoRemotoClipboard.ApplyRemoteTextToLocalClipboard(clipText);
                    }
                    break;

                case AcessoRemotoProtocol.TypeClipboardFilesAdvertisement:
                    if (root.TryGetProperty("clipboard_files", out var cfProp) && cfProp.ValueKind == JsonValueKind.Array)
                    {
                        var filesAd = JsonSerializer.Deserialize<List<AcessoRemotoClipboardFileItem>>(cfProp.GetRawText());
                        if (filesAd is { Count: > 0 })
                        {
                            AcessoRemotoClipboard.ApplyRemoteFilesToLocalClipboard(
                                filesAd,
                                fileIdx => OpenPullStreamFromHost(filesAd, fileIdx));
                        }
                    }
                    break;

                case AcessoRemotoProtocol.TypeClipboardFileRequest:
                    var reqIdx = root.TryGetProperty("file_index", out var fiProp) ? fiProp.GetInt32() : -1;
                    var reqTransferId = root.TryGetProperty("transfer_id", out var tidProp) ? tidProp.GetString() ?? "" : "";
                    if (reqIdx >= 0 && !string.IsNullOrEmpty(reqTransferId))
                    {
                        _ = Task.Run(() => HandleViewerClipboardFileRequestAsync(reqIdx, reqTransferId));
                    }
                    break;

                case AcessoRemotoProtocol.TypeClipboardFileChunk:
                    var chunkTransferId = root.TryGetProperty("transfer_id", out var ctidProp) ? ctidProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(chunkTransferId) &&
                        _viewerClipboardPullStreams.TryGetValue(chunkTransferId, out var pullStream))
                    {
                        var chunkErr = root.TryGetProperty("message", out var ceProp) ? ceProp.GetString() : null;
                        var chunkB64Str = root.TryGetProperty("chunk_b64", out var cbProp) ? cbProp.GetString() : null;
                        var chunkIsLast = root.TryGetProperty("is_last", out var cilProp) && cilProp.GetBoolean();
                        byte[] chunkBytes = !string.IsNullOrEmpty(chunkB64Str)
                            ? Convert.FromBase64String(chunkB64Str)
                            : Array.Empty<byte>();

                        pullStream.PushChunk(chunkBytes, chunkIsLast, chunkErr);
                        if (chunkIsLast || !string.IsNullOrEmpty(chunkErr))
                        {
                            _viewerClipboardPullStreams.TryRemove(chunkTransferId, out _);
                        }
                    }
                    break;

                case "fs_list_res":
                    var currentPath = root.TryGetProperty("path", out var p) ? p.GetString() ?? "C:\\" : "C:\\";
                    var drives = new List<string>();
                    if (root.TryGetProperty("drives", out var dProp) && dProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dProp.EnumerateArray())
                            drives.Add(d.GetString() ?? "");
                    }

                    var items = new List<RemoteViewerFileItem>();
                    if (root.TryGetProperty("entries", out var eProp) && eProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in eProp.EnumerateArray())
                        {
                            items.Add(new RemoteViewerFileItem
                            {
                                Name = item.GetProperty("name").GetString() ?? "",
                                IsDir = item.GetProperty("is_dir").GetBoolean(),
                                Size = item.GetProperty("size").GetInt64(),
                                ModTime = item.GetProperty("mod_time").GetString() ?? "",
                            });
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        _currentRemotePath = currentPath;
                        TxtCurrentPath.Text = currentPath;

                        PnlDrives.Children.Clear();
                        foreach (var drive in drives)
                        {
                            var isCurrent = currentPath.StartsWith(drive, StringComparison.OrdinalIgnoreCase);
                            var btn = new Button
                            {
                                Background = isCurrent
                                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"))
                                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                                Foreground = isCurrent
                                    ? Brushes.White
                                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                                BorderBrush = isCurrent
                                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"))
                                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")),
                                Padding = new Thickness(10, 0, 10, 0),
                                Margin = new Thickness(0, 0, 6, 0),
                                Height = 36,
                                Cursor = Cursors.Hand
                            };

                            var driveSp = new StackPanel
                            {
                                Orientation = System.Windows.Controls.Orientation.Horizontal,
                                VerticalAlignment = System.Windows.VerticalAlignment.Center
                            };
                            driveSp.Children.Add(new System.Windows.Shapes.Path
                            {
                                Data = System.Windows.Media.Geometry.Parse("M 4,4 H 20 A 2,2 0 0 1 22,6 V 18 A 2,2 0 0 1 20,20 H 4 A 2,2 0 0 1 2,18 V 6 A 2,2 0 0 1 4,4 Z M 16,14 H 18 M 6,14 H 12"),
                                Stroke = isCurrent ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                                StrokeThickness = 1.6,
                                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                                Width = 13,
                                Height = 13,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 6, 0)
                            });
                            driveSp.Children.Add(new TextBlock
                            {
                                Text = drive,
                                FontSize = 12,
                                FontWeight = System.Windows.FontWeights.SemiBold,
                                VerticalAlignment = System.Windows.VerticalAlignment.Center
                            });
                            btn.Content = driveSp;

                            btn.Click += (_, _) => SendJson(new { type = "fs_list", path = drive });
                            PnlDrives.Children.Add(btn);
                        }

                        _fileItems.Clear();
                        int folderCount = 0;
                        int fileCount = 0;
                        foreach (var fi in items)
                        {
                            _fileItems.Add(fi);
                            if (fi.IsDir) folderCount++; else fileCount++;
                        }
                        if (LblItemsCount != null)
                            LblItemsCount.Text = $"{_fileItems.Count} itens ({folderCount} pastas, {fileCount} arquivos)";
                    });
                    break;

                case "fs_download_chunk":
                    var downloadErrMsg = root.TryGetProperty("message", out var dErrProp) ? dErrProp.GetString() : null;
                    if (!string.IsNullOrEmpty(downloadErrMsg))
                    {
                        _downloadStream?.Dispose();
                        _downloadStream = null;
                        if (!string.IsNullOrEmpty(_downloadDestPath) && File.Exists(_downloadDestPath))
                        {
                            try { File.Delete(_downloadDestPath); } catch { }
                        }
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgressBar.Value = 0;
                            LblTransferStatus.Text = $"Falha: {downloadErrMsg}";
                            MessageBox.Show($"Falha no download:\n{downloadErrMsg}", "Erro de Transferência", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        break;
                    }
                    var b64 = root.TryGetProperty("chunk_b64", out var bProp) ? bProp.GetString() ?? "" : "";
                    var offset = root.TryGetProperty("offset", out var oProp) ? oProp.GetInt64() : 0;
                    var totalSize = root.TryGetProperty("total_size", out var tsProp) ? tsProp.GetInt64() : 1;
                    var isLast = root.TryGetProperty("is_last", out var ilProp) && ilProp.GetBoolean();

                    if (!string.IsNullOrEmpty(b64) && _downloadStream != null)
                    {
                        var bytes = Convert.FromBase64String(b64);
                        _downloadStream.Write(bytes, 0, bytes.Length);

                        var percent = totalSize > 0 ? (int)((offset + bytes.Length) * 100 / totalSize) : 100;
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgressBar.Value = Math.Min(100, Math.Max(0, percent));
                            LblTransferStatus.Text = $"Baixando... {percent}% ({(offset + bytes.Length) / 1024} KB de {totalSize / 1024} KB)";
                        });
                    }

                    if (isLast)
                    {
                        _downloadStream?.Dispose();
                        _downloadStream = null;
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgressBar.Value = 100;
                            LblTransferStatus.Text = "Download concluído com sucesso!";
                            MessageBox.Show($"Arquivo salvo com sucesso em:\n{_downloadDestPath}", "Download Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    break;

                case "error":
                    var msg = root.TryGetProperty("message", out var mErr) ? mErr.GetString() : "Erro desconhecido";
                    if (_isReconnectingWithPassword)
                    {
                        _isReconnectingWithPassword = false;
                        _waitingAuthTcs?.TrySetResult((false, msg));
                        _ = Dispatcher.InvokeAsync(async () =>
                        {
                            ResetWaitingPasswordUiState(clearPassword: false);
                            ShowWaitingPasswordError(string.IsNullOrWhiteSpace(msg)
                                ? "Senha incorreta. Verifique a senha e tente novamente."
                                : msg!);
                            FocusWaitingPasswordInput(selectAll: true);
                            await ReconnectWaitingWithoutPasswordAsync();
                        });
                        break;
                    }
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        await EndSessionAndCloseAsync(displayMessageToTechnician: msg);
                    });
                    break;

                case "session_ended":
                    var endMsg = root.TryGetProperty("message", out var mEnded) ? mEnded.GetString() : "A sessão remota foi finalizada pelo cliente.";
                    if (_isReconnectingWithPassword)
                        break;
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        var wasWaiting = WaitingCard.Visibility == Visibility.Visible;
                        await EndSessionAndCloseAsync(
                            reason: endMsg,
                            displayMessageToTechnician: wasWaiting ? (endMsg ?? "A solicitação de acesso remoto foi recusada pelo utilizador.") : null);
                    });
                    break;
            }
        }
        catch { }
    }

    private void ProcessBinaryPacket(byte[] buffer, int length)
    {
        if (length < 1) return;
        byte opcode = buffer[0];

        if (opcode == OpFrame)
        {
            if (length < 9) return;

            uint w = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(1, 4));
            uint h = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(5, 4));

            var jpegStream = new MemoryStream(buffer, 9, length - 9);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = jpegStream;
            bmp.EndInit();
            bmp.Freeze();

            Dispatcher.Invoke(() =>
            {
                MarkSessionAccepted();
                if (DisconnectedCard.Visibility != Visibility.Collapsed)
                    DisconnectedCard.Visibility = Visibility.Collapsed;

                ImgRemoteScreen.Source = bmp;
                _remoteWidth = (int)w;
                _remoteHeight = (int)h;

                _frameCount++;
                var elapsed = (DateTime.UtcNow - _lastFpsCheck).TotalSeconds;
                if (elapsed >= 1.0)
                {
                    var fps = Math.Round(_frameCount / elapsed);
                    LblFpsInfo.Text = $"{_remoteWidth}x{_remoteHeight} • {fps} FPS";
                    _frameCount = 0;
                    _lastFpsCheck = DateTime.UtcNow;
                }

                if (_lastRemoteCursorState != null && _showRemoteCursor && (_isTechnicianBlockedByClient || _lastRemoteCursorState.ClientActive))
                {
                    UpdateClientCursorVisual(_lastRemoteCursorState);
                }
                else if (ClientCursorContainer != null && ClientCursorContainer.Visibility != Visibility.Collapsed)
                {
                    ClientCursorContainer.Visibility = Visibility.Collapsed;
                }
            });
        }
        else if (opcode == OpCursor)
        {
            if (AcessoRemotoCursorHelper.TryParsePacket(buffer.AsSpan(0, length), out var cursorState))
            {
                Dispatcher.InvokeAsync(() => ApplyRemoteCursorState(cursorState));
            }
        }
    }

    private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _isReconnectingWithPassword = false;
        _waitingAuthTcs?.TrySetCanceled();
        await EndSessionAndCloseAsync("Desconectado pelo técnico.", displayMessageToTechnician: null);
    }

    private void MarkSessionAccepted(bool unattended = false)
    {
        _isReconnectingWithPassword = false;
        _waitingAuthTcs?.TrySetResult((true, null));
        if (WaitingCard.Visibility != Visibility.Visible) return;

        WaitingCard.Visibility = Visibility.Collapsed;
        ResetWaitingPasswordUiState(clearPassword: true);
        LblStatusText.Text = unattended
            ? "Conectado (Acesso não supervisionado)"
            : $"Conectado • {TxtTargetId.Text.Trim()}";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
        ImgRemoteScreen.Focus();
    }

    private bool _isWaitingPasswordRevealed;
    private bool _syncingWaitingPasswordText;
    private bool _isReconnectingWithPassword;
    private TaskCompletionSource<(bool Success, string? Message)>? _waitingAuthTcs;

    private string GetWaitingPasswordInput()
    {
        var raw = _isWaitingPasswordRevealed
            ? TxtWaitingPasswordRevealed.Text
            : PwdWaitingPassword.Password;
        return (raw ?? "").Trim();
    }

    private void UpdateWaitingPasswordHintVisibility()
    {
        bool hasText = !string.IsNullOrEmpty(PwdWaitingPassword.Password)
                       || !string.IsNullOrEmpty(TxtWaitingPasswordRevealed.Text);
        HintWaitingPassword.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PwdWaitingPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncingWaitingPasswordText)
        {
            _syncingWaitingPasswordText = true;
            TxtWaitingPasswordRevealed.Text = PwdWaitingPassword.Password;
            _syncingWaitingPasswordText = false;
        }
        UpdateWaitingPasswordHintVisibility();
        if (LblWaitingPasswordError.Visibility == Visibility.Visible)
            LblWaitingPasswordError.Visibility = Visibility.Collapsed;
    }

    private void TxtWaitingPasswordRevealed_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingWaitingPasswordText)
        {
            _syncingWaitingPasswordText = true;
            PwdWaitingPassword.Password = TxtWaitingPasswordRevealed.Text;
            _syncingWaitingPasswordText = false;
        }
        UpdateWaitingPasswordHintVisibility();
        if (LblWaitingPasswordError.Visibility == Visibility.Visible)
            LblWaitingPasswordError.Visibility = Visibility.Collapsed;
    }

    private void BtnToggleWaitingPasswordEye_Click(object sender, RoutedEventArgs e)
    {
        _isWaitingPasswordRevealed = !_isWaitingPasswordRevealed;
        if (_isWaitingPasswordRevealed)
        {
            TxtWaitingPasswordRevealed.Text = PwdWaitingPassword.Password;
            PwdWaitingPassword.Visibility = Visibility.Collapsed;
            TxtWaitingPasswordRevealed.Visibility = Visibility.Visible;
            IconWaitingPasswordEye.Text = "\uED1A";
            TxtWaitingPasswordRevealed.CaretIndex = TxtWaitingPasswordRevealed.Text.Length;
            TxtWaitingPasswordRevealed.Focus();
        }
        else
        {
            PwdWaitingPassword.Password = TxtWaitingPasswordRevealed.Text;
            TxtWaitingPasswordRevealed.Visibility = Visibility.Collapsed;
            PwdWaitingPassword.Visibility = Visibility.Visible;
            IconWaitingPasswordEye.Text = "\uE7B3";
            PwdWaitingPassword.Focus();
        }
        UpdateWaitingPasswordHintVisibility();
    }

    private void FocusWaitingPasswordInput(bool selectAll)
    {
        if (_isWaitingPasswordRevealed)
        {
            TxtWaitingPasswordRevealed.Focus();
            if (selectAll)
                TxtWaitingPasswordRevealed.SelectAll();
        }
        else
        {
            PwdWaitingPassword.Focus();
            if (selectAll)
                PwdWaitingPassword.SelectAll();
        }
    }

    private void ShowWaitingPasswordError(string message)
    {
        LblWaitingPasswordError.Text = message;
        LblWaitingPasswordError.Visibility = Visibility.Visible;
    }

    private (string Name, string AvatarBase64) GetEffectiveTechnicianInfo()
    {
        if (GlpiTechnicianSession.IsAuthenticated && !string.IsNullOrWhiteSpace(GlpiTechnicianSession.TechnicianName))
        {
            return (GlpiTechnicianSession.TechnicianName.Trim(), GlpiTechnicianSession.TechnicianAvatarBase64 ?? "");
        }

        // Se não houver login no GLPI, ao invés de mostrar técnico de suporte, mostra o nome do usuário do Windows (sem foto)
        var winUser = Environment.UserName?.Trim();
        var fallbackName = !string.IsNullOrWhiteSpace(winUser) ? winUser : "Técnico de Suporte";
        return (fallbackName, "");
    }

    private void UpdateWaitingGlpiAuthState()
    {
        var isAuth = GlpiTechnicianSession.IsAuthenticated;
        if (isAuth)
        {
            WaitingPasswordSection.Visibility = Visibility.Visible;
            WaitingSupervisedNotice.Visibility = Visibility.Collapsed;
        }
        else
        {
            WaitingPasswordSection.Visibility = Visibility.Collapsed;
            WaitingSupervisedNotice.Visibility = Visibility.Visible;
        }
    }

    private void BtnWaitingGlpiLogin_Click(object sender, RoutedEventArgs e)
    {
        GlpiLoginWindow.PromptLogin(this);
    }

    private void ResetWaitingPasswordUiState(bool clearPassword)
    {
        BtnSubmitWaitingPassword.IsEnabled = true;
        BtnSubmitWaitingPassword.Content = "Entrar com senha";
        PwdWaitingPassword.IsEnabled = true;
        TxtWaitingPasswordRevealed.IsEnabled = true;
        BtnToggleWaitingPasswordEye.IsEnabled = true;

        if (clearPassword)
        {
            _syncingWaitingPasswordText = true;
            PwdWaitingPassword.Password = "";
            TxtWaitingPasswordRevealed.Text = "";
            _syncingWaitingPasswordText = false;
            LblWaitingPasswordError.Visibility = Visibility.Collapsed;
            LblWaitingPasswordError.Text = "";
        }
        UpdateWaitingPasswordHintVisibility();
    }

    private async void WaitingPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitWaitingPasswordAsync();
        }
    }

    private async void BtnSubmitWaitingPassword_Click(object sender, RoutedEventArgs e)
    {
        await SubmitWaitingPasswordAsync();
    }

    private async Task SubmitWaitingPasswordAsync()
    {
        if (!BtnSubmitWaitingPassword.IsEnabled)
            return;

        if (!GlpiTechnicianSession.IsAuthenticated)
        {
            ShowWaitingPasswordError("Acesso com senha restrito: faça login na sua conta.");
            return;
        }

        var pwd = GetWaitingPasswordInput();
        if (string.IsNullOrEmpty(pwd))
        {
            ShowWaitingPasswordError("Informe a senha de acesso para continuar.");
            FocusWaitingPasswordInput(selectAll: false);
            return;
        }

        LblWaitingPasswordError.Visibility = Visibility.Collapsed;
        BtnSubmitWaitingPassword.IsEnabled = false;
        BtnSubmitWaitingPassword.Content = "Validando senha...";
        PwdWaitingPassword.IsEnabled = false;
        TxtWaitingPasswordRevealed.IsEnabled = false;
        BtnToggleWaitingPasswordEye.IsEnabled = false;

        _waitingAuthTcs?.TrySetCanceled();
        var authTcs = new TaskCompletionSource<(bool Success, string? Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waitingAuthTcs = authTcs;

        var (techName, techAvatar) = GetEffectiveTechnicianInfo();

        await SendJsonAsync(new
        {
            type = AcessoRemotoProtocol.TypeAuthPassword,
            password = pwd,
            technician_name = techName,
            technician_avatar = techAvatar
        });

        var completed = await Task.WhenAny(authTcs.Task, Task.Delay(3200));
        if (completed == authTcs.Task && authTcs.Task.IsCompletedSuccessfully)
        {
            var (ok, errMsg) = await authTcs.Task;
            if (ok)
            {
                TxtPassword.Password = pwd;
                MarkSessionAccepted(unattended: true);
                return;
            }

            ResetWaitingPasswordUiState(clearPassword: false);
            ShowWaitingPasswordError(string.IsNullOrWhiteSpace(errMsg)
                ? "Senha incorreta. Verifique a senha e tente novamente."
                : errMsg!);
            FocusWaitingPasswordInput(selectAll: true);
            return;
        }

        if (_isClosingSession || WaitingCard.Visibility != Visibility.Visible)
            return;

        await TryReconnectWithPasswordFallbackAsync(pwd);
    }

    private async Task TryReconnectWithPasswordFallbackAsync(string pwd)
    {
        var serverUrl = TxtServerUrl.Text.Trim();
        var targetId = TxtTargetId.Text.Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(targetId))
        {
            ResetWaitingPasswordUiState(clearPassword: false);
            return;
        }

        _isReconnectingWithPassword = true;
        var oldWs = _ws;
        var oldCts = _cts;

        var newCts = new CancellationTokenSource();
        var newWs = new ClientWebSocket();
        newWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

        _cts = newCts;
        _ws = newWs;

        try
        {
            if (oldWs != null && (oldWs.State == WebSocketState.Open || oldWs.State == WebSocketState.CloseReceived))
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
                await _wsSendLock.WaitAsync(closeCts.Token);
                try
                {
                    if (oldWs.State == WebSocketState.Open || oldWs.State == WebSocketState.CloseReceived)
                        await oldWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", closeCts.Token);
                }
                finally
                {
                    _wsSendLock.Release();
                }
            }
        }
        catch { }

        try { oldCts?.Cancel(); } catch { }
        try { oldWs?.Dispose(); } catch { }

        try
        {
            await Task.Delay(150, newCts.Token);
            await newWs.ConnectAsync(new Uri(serverUrl), newCts.Token);

            var (techName, techAvatar) = GetEffectiveTechnicianInfo();

            var authPayload = JsonSerializer.Serialize(new
            {
                type = "connect_viewer",
                target_id = targetId,
                password = pwd,
                technician_name = techName,
                technician_avatar = techAvatar
            });

            var authBytes = Encoding.UTF8.GetBytes(authPayload);
            await newWs.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true, newCts.Token);

            _ = Task.Run(() => ReceiveLoopAsync(newWs, newCts.Token));
        }
        catch (Exception ex)
        {
            _isReconnectingWithPassword = false;
            ResetWaitingPasswordUiState(clearPassword: false);
            ShowWaitingPasswordError($"Falha ao validar senha: {ex.Message}");
            FocusWaitingPasswordInput(selectAll: true);
        }
    }

    private async Task ReconnectWaitingWithoutPasswordAsync()
    {
        if (_isClosingSession)
            return;

        var serverUrl = TxtServerUrl.Text.Trim();
        var targetId = TxtTargetId.Text.Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(targetId))
            return;

        var oldWs = _ws;
        var oldCts = _cts;

        var newCts = new CancellationTokenSource();
        var newWs = new ClientWebSocket();
        newWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

        _cts = newCts;
        _ws = newWs;

        try
        {
            if (oldWs != null && (oldWs.State == WebSocketState.Open || oldWs.State == WebSocketState.CloseReceived))
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await oldWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", closeCts.Token);
            }
        }
        catch { }

        try { oldCts?.Cancel(); } catch { }
        try { oldWs?.Dispose(); } catch { }

        try
        {
            await Task.Delay(150, newCts.Token);
            await newWs.ConnectAsync(new Uri(serverUrl), newCts.Token);

            var (techName, techAvatar) = GetEffectiveTechnicianInfo();

            var authPayload = JsonSerializer.Serialize(new
            {
                type = "connect_viewer",
                target_id = targetId,
                password = "",
                technician_name = techName,
                technician_avatar = techAvatar
            });

            var authBytes = Encoding.UTF8.GetBytes(authPayload);
            await newWs.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true, newCts.Token);

            _ = Task.Run(() => ReceiveLoopAsync(newWs, newCts.Token));
        }
        catch { }
    }

    private bool _isClosingSession;

    private async Task EndSessionAndCloseAsync(string? reason = null, string? displayMessageToTechnician = null)
    {
        if (_isClosingSession) return;
        _isClosingSession = true;
        _isReconnectingWithPassword = false;
        _waitingAuthTcs?.TrySetCanceled();

        GlpiLoginWindow.CloseActiveInstance();
        GlpiRememberLoginConfirmDialog.CloseActiveInstance();

        try
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    type = "session_ended",
                    message = reason ?? "Sessão encerrada pelo técnico."
                });
                var bytes = Encoding.UTF8.GetBytes(payload);
                using var sendCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                await _wsSendLock.WaitAsync(sendCts.Token);
                try
                {
                    if (_ws != null && _ws.State == WebSocketState.Open)
                        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, sendCts.Token);
                }
                finally
                {
                    _wsSendLock.Release();
                }
            }
        }
        catch { }

        DisconnectInternal();

        Close();

        if (!string.IsNullOrWhiteSpace(displayMessageToTechnician))
        {
            AcessoRemotoRecusadoDialog.ShowDialog(displayMessageToTechnician);
        }
    }

    private void ApplyRemoteCursorState(RemoteCursorState state)
    {
        _lastRemoteCursorState = state;
        ApplyTechnicianCursor(state.Type);
        UpdateClientCursorVisual(state);
    }

    private System.Windows.Input.Cursor GetTechnicianWpfCursor(RemoteCursorType type)
    {
        return type switch
        {
            RemoteCursorType.Hand => Cursors.Hand,
            RemoteCursorType.IBeam => Cursors.IBeam,
            RemoteCursorType.Wait => Cursors.Wait,
            RemoteCursorType.Cross => Cursors.Cross,
            RemoteCursorType.SizeWE => Cursors.SizeWE,
            RemoteCursorType.SizeNS => Cursors.SizeNS,
            RemoteCursorType.SizeNWSE => Cursors.SizeNWSE,
            RemoteCursorType.SizeNESW => Cursors.SizeNESW,
            RemoteCursorType.SizeAll => Cursors.SizeAll,
            RemoteCursorType.No => Cursors.No,
            RemoteCursorType.AppStarting => Cursors.AppStarting,
            RemoteCursorType.Help => Cursors.Help,
            RemoteCursorType.UpArrow => Cursors.UpArrow,
            _ => Cursors.Arrow
        };
    }

    private void ImgRemoteScreen_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (_isTechnicianBlockedByClient)
        {
            e.Cursor = Cursors.No;
            e.Handled = true;
            return;
        }

        e.Cursor = GetTechnicianWpfCursor(_currentTechnicianCursorType);
        e.Handled = true;
    }

    private void ViewScreen_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (WaitingCard.Visibility == Visibility.Visible || DisconnectedCard.Visibility == Visibility.Visible)
            return;

        if (_isTechnicianBlockedByClient)
        {
            e.Cursor = Cursors.No;
            e.Handled = true;
            return;
        }

        e.Cursor = GetTechnicianWpfCursor(_currentTechnicianCursorType);
        e.Handled = true;
    }

    private void ApplyTechnicianCursor(RemoteCursorType type)
    {
        _currentTechnicianCursorType = type;

        var targetCursor = _isTechnicianBlockedByClient
            ? Cursors.No
            : GetTechnicianWpfCursor(type);

        if (!ReferenceEquals(ImgRemoteScreen.Cursor, targetCursor))
        {
            ImgRemoteScreen.Cursor = targetCursor;
            ViewScreen.Cursor = targetCursor;
        }

        try
        {
            Mouse.UpdateCursor();
        }
        catch
        {
        }
    }

    private void UpdateClientCursorVisual(RemoteCursorState state)
    {
        if (ClientCursorContainer == null || ImgRemoteScreen == null || CanvasCursorOverlay == null)
            return;

        // O cursor visual do cliente SÓ deve ser exibido na tela do técnico quando:
        // 1. A opção de visualização estiver ativada (_showRemoteCursor)
        // 2. A imagem da tela remota estiver renderizada e visível
        // 3. O cliente físico estiver em atividade recente (_isTechnicianBlockedByClient == true ou state.ClientActive == true)
        bool shouldShowClientCursor = _showRemoteCursor &&
                                      state.Visible &&
                                      (_isTechnicianBlockedByClient || state.ClientActive) &&
                                      ImgRemoteScreen.Source is BitmapSource &&
                                      ImgRemoteScreen.IsLoaded &&
                                      ImgRemoteScreen.IsVisible &&
                                      ImgRemoteScreen.ActualWidth > 0 &&
                                      ImgRemoteScreen.ActualHeight > 0;

        if (!shouldShowClientCursor)
        {
            if (ClientCursorContainer.Visibility != Visibility.Collapsed)
                ClientCursorContainer.Visibility = Visibility.Collapsed;
            return;
        }

        float nx = Math.Clamp(state.NormX, 0f, 1f);
        float ny = Math.Clamp(state.NormY, 0f, 1f);

        try
        {
            // Converte a coordenada normalizada (0..1) sobre a imagem remota diretamente para o sistema de coordenadas do Canvas
            System.Windows.Point pointOnCanvas = ImgRemoteScreen.TranslatePoint(
                new System.Windows.Point(nx * ImgRemoteScreen.ActualWidth, ny * ImgRemoteScreen.ActualHeight),
                CanvasCursorOverlay);

            Canvas.SetLeft(ClientCursorContainer, pointOnCanvas.X);
            Canvas.SetTop(ClientCursorContainer, pointOnCanvas.Y);

            if (BadgeClientCursor != null && TxtClientBadge != null)
            {
                BadgeClientCursor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                BadgeClientCursor.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                TxtClientBadge.Text = "Cliente Ativo";
                TxtClientBadge.Foreground = Brushes.White;
            }

            if (ClientCursorContainer.Visibility != Visibility.Visible)
                ClientCursorContainer.Visibility = Visibility.Visible;
        }
        catch
        {
            // Ignora se o elemento estiver temporariamente desconectado da árvore visual
        }
    }

    private void ImgRemoteScreen_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lastRemoteCursorState != null && _showRemoteCursor && (_isTechnicianBlockedByClient || _lastRemoteCursorState.ClientActive))
        {
            UpdateClientCursorVisual(_lastRemoteCursorState);
        }
    }

    private void BtnToggleRemoteCursor_Click(object sender, RoutedEventArgs e)
    {
        _showRemoteCursor = !_showRemoteCursor;
        UpdateRemoteCursorToggleUi();
    }

    private void UpdateRemoteCursorToggleUi()
    {
        if (BtnToggleRemoteCursor == null || PathToggleRemoteCursor == null)
            return;

        if (_showRemoteCursor)
        {
            BtnToggleRemoteCursor.ToolTip = "Cursor do cliente visível (Clique para ocultar)";
            PathToggleRemoteCursor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
            PathToggleRemoteCursor.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5B4FC"));
            if (_lastRemoteCursorState != null && (_lastRemoteCursorState.ClientActive || _isTechnicianBlockedByClient))
                UpdateClientCursorVisual(_lastRemoteCursorState);
        }
        else
        {
            BtnToggleRemoteCursor.ToolTip = "Cursor do cliente oculto (Clique para exibir)";
            PathToggleRemoteCursor.Fill = Brushes.Transparent;
            PathToggleRemoteCursor.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            if (ClientCursorContainer != null)
                ClientCursorContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void SetTechnicianInputBlockedByClient(bool blocked)
    {
        _isTechnicianBlockedByClient = blocked;

        if (_lastRemoteCursorState != null)
        {
            _lastRemoteCursorState.ClientActive = blocked;
        }

        ApplyTechnicianCursor(_currentTechnicianCursorType);

        if (!blocked && ClientCursorContainer != null)
        {
            ClientCursorContainer.Visibility = Visibility.Collapsed;
        }

        if (PillClientPriority != null)
        {
            PillClientPriority.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void DisconnectInternal()
    {
        _isConnected = false;
        _isInputBlocked = false;
        SetTechnicianInputBlockedByClient(false);
        _lastRemoteCursorState = null;
        ApplyTechnicianCursor(RemoteCursorType.Arrow);
        if (ClientCursorContainer != null)
            ClientCursorContainer.Visibility = Visibility.Collapsed;
        WaitingCard.Visibility = Visibility.Collapsed;
        DisconnectedCard.Visibility = Visibility.Collapsed;

        foreach (var kv in _viewerClipboardPullStreams)
        {
            if (_viewerClipboardPullStreams.TryRemove(kv.Key, out var ps))
                ps.PushChunk(Array.Empty<byte>(), true, "Sessão remota encerrada durante a transferência.");
        }

        try
        {
            _cts?.Cancel();
            _ws?.Dispose();
            _downloadStream?.Dispose();
        }
        catch { }

        _ws = null;
        _cts = null;
        _downloadStream = null;

        SetConnectionState(connected: false);
        SetChatOpen(false);
        ImgRemoteScreen.Source = null;
        _fileItems.Clear();
        PnlDrives.Children.Clear();
        if (LblItemsCount != null) LblItemsCount.Text = "0 itens";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_isClosingSession)
        {
            _isClosingSession = true;
            _isReconnectingWithPassword = false;
            _waitingAuthTcs?.TrySetCanceled();
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open && _wsSendLock.Wait(200))
                {
                    try
                    {
                        var payload = JsonSerializer.Serialize(new
                        {
                            type = "session_ended",
                            message = "Sessão encerrada pelo técnico."
                        });
                        var bytes = Encoding.UTF8.GetBytes(payload);
                        using var sendCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                        _ = _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, sendCts.Token);
                    }
                    finally
                    {
                        _wsSendLock.Release();
                    }
                }
            }
            catch { }
            DisconnectInternal();
        }
    }

    private void SetConnectionState(bool connected = false, bool connecting = false)
    {
        BtnConnect.IsEnabled = !connected && !connecting;
        BtnDisconnect.IsEnabled = connected || connecting;
        BtnBlockInput.IsEnabled = connected;
        MenuQuickActions.IsEnabled = connected;
        PnlMonitors.IsEnabled = connected;
        TxtServerUrl.IsEnabled = !connected && !connecting;
        TxtTargetId.IsEnabled = !connected && !connecting;
        TxtPassword.IsEnabled = !connected && !connecting;
    }

    #endregion

    #region Input Handling

    private (float normX, float normY)? GetNormalizedCoordinates(MouseEventArgs e)
    {
        if (!_isConnected || ImgRemoteScreen.Source is not BitmapSource bmp || ImgRemoteScreen.ActualWidth <= 0 || ImgRemoteScreen.ActualHeight <= 0)
            return null;

        var pos = e.GetPosition(ImgRemoteScreen);

        double elemW = ImgRemoteScreen.ActualWidth;
        double elemH = ImgRemoteScreen.ActualHeight;
        double srcW = bmp.PixelWidth;
        double srcH = bmp.PixelHeight;

        if (srcW <= 0 || srcH <= 0) return null;

        double scale = Math.Min(elemW / srcW, elemH / srcH);
        double renderW = srcW * scale;
        double renderH = srcH * scale;

        double offsetX = (elemW - renderW) / 2.0;
        double offsetY = (elemH - renderH) / 2.0;

        double imgX = pos.X - offsetX;
        double imgY = pos.Y - offsetY;

        if (imgX < 0 || imgX > renderW || imgY < 0 || imgY > renderH)
            return null;

        float nx = (float)(imgX / renderW);
        float ny = (float)(imgY / renderH);

        return (Math.Clamp(nx, 0f, 1f), Math.Clamp(ny, 0f, 1f));
    }

    private void ImgRemoteScreen_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewScreen.Visibility != Visibility.Visible) return;
        if (_isTechnicianBlockedByClient)
        {
            ApplyTechnicianCursor(RemoteCursorType.No);
            return;
        }

        if (ClientCursorContainer != null && ClientCursorContainer.Visibility != Visibility.Collapsed)
        {
            ClientCursorContainer.Visibility = Visibility.Collapsed;
        }

        if (_lastRemoteCursorState != null)
        {
            _lastRemoteCursorState.ClientActive = false;
        }

        var coords = GetNormalizedCoordinates(e);
        if (!coords.HasValue) return;

        var packet = new byte[10];
        packet[0] = OpInput;
        packet[1] = InputMouseMove;
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(2, 4), coords.Value.normX);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(6, 4), coords.Value.normY);
        SendInputPacket(packet);
    }

    private void ImgRemoteScreen_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isConnected || ViewScreen.Visibility != Visibility.Visible || _isTechnicianBlockedByClient) return;
        ImgRemoteScreen.Focus();

        var coords = GetNormalizedCoordinates(e);
        if (!coords.HasValue) return;

        byte btn = e.ChangedButton switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 3,
            _ => 0
        };
        if (btn == 0) return;

        var packet = new byte[11];
        packet[0] = OpInput;
        packet[1] = InputMouseDown;
        packet[2] = btn;
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(3, 4), coords.Value.normX);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(7, 4), coords.Value.normY);
        SendInputPacket(packet);
    }

    private void ImgRemoteScreen_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isConnected || ViewScreen.Visibility != Visibility.Visible || _isTechnicianBlockedByClient) return;

        var coords = GetNormalizedCoordinates(e);
        if (!coords.HasValue) return;

        byte btn = e.ChangedButton switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 3,
            _ => 0
        };
        if (btn == 0) return;

        var packet = new byte[11];
        packet[0] = OpInput;
        packet[1] = InputMouseUp;
        packet[2] = btn;
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(3, 4), coords.Value.normX);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(7, 4), coords.Value.normY);
        SendInputPacket(packet);
    }

    private void ImgRemoteScreen_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isConnected || ViewScreen.Visibility != Visibility.Visible || _isTechnicianBlockedByClient) return;

        var coords = GetNormalizedCoordinates(e);
        float nx = coords?.normX ?? 0.5f;
        float ny = coords?.normY ?? 0.5f;

        var packet = new byte[14];
        packet[0] = OpInput;
        packet[1] = InputMouseWheel;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(2, 4), e.Delta);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(6, 4), nx);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(10, 4), ny);
        SendInputPacket(packet);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isConnected || ViewScreen.Visibility != Visibility.Visible || _isTechnicianBlockedByClient) return;
        if (TxtChatMessage.IsFocused || TxtCurrentPath.IsFocused || TxtServerUrl.IsFocused || TxtTargetId.IsFocused || TxtPassword.IsFocused ||
            PwdWaitingPassword.IsFocused || PwdWaitingPassword.IsKeyboardFocused ||
            TxtWaitingPasswordRevealed.IsFocused || TxtWaitingPasswordRevealed.IsKeyboardFocused ||
            TxtNewFolderName.IsFocused)
            return;

        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (WaitingCard.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SubmitWaitingPasswordAsync();
            }
            return;
        }

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(actualKey);
        if (vk > 0)
        {
            if (vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU)
                _remoteAltKeyDown = true;
            else if (vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL)
                _remoteCtrlKeyDown = true;
            else if (vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT)
                _remoteShiftKeyDown = true;

            var packet = new byte[4];
            packet[0] = OpInput;
            packet[1] = InputKeyDown;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)vk);
            SendInputPacket(packet);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_isConnected || ViewScreen.Visibility != Visibility.Visible || WaitingCard.Visibility == Visibility.Visible || _isTechnicianBlockedByClient) return;
        if (TxtChatMessage.IsFocused || TxtCurrentPath.IsFocused || TxtServerUrl.IsFocused || TxtTargetId.IsFocused || TxtPassword.IsFocused ||
            PwdWaitingPassword.IsFocused || PwdWaitingPassword.IsKeyboardFocused ||
            TxtWaitingPasswordRevealed.IsFocused || TxtWaitingPasswordRevealed.IsKeyboardFocused ||
            TxtNewFolderName.IsFocused)
            return;

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(actualKey);
        if (vk > 0)
        {
            if (vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU)
                _remoteAltKeyDown = false;
            else if (vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL)
                _remoteCtrlKeyDown = false;
            else if (vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT)
                _remoteShiftKeyDown = false;

            var packet = new byte[4];
            packet[0] = OpInput;
            packet[1] = InputKeyUp;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)vk);
            SendInputPacket(packet);
            e.Handled = true;
        }
    }

    private void OnLocalViewerClipboardChanged()
    {
        if (!_isConnected)
            return;

        var (text, files) = AcessoRemotoClipboard.ReadLocalClipboard();
        if (files is { Count: > 0 })
        {
            _ = Task.Run(async () =>
            {
                var ads = AcessoRemotoClipboard.BuildClipboardFilesAdvertisement(files);
                if (ads.Count > 0)
                {
                    _viewerAdvertisedFiles = ads;
                    var wireAds = ads.Select(a => new AcessoRemotoClipboardFileItem
                    {
                        Index = a.Index,
                        Name = a.Name,
                        RelativePath = a.RelativePath,
                        Size = a.Size,
                        IsDir = a.IsDir,
                        LastWriteUtcTicks = a.LastWriteUtcTicks
                    }).ToList();

                    await SendJsonAsync(new
                    {
                        type = AcessoRemotoProtocol.TypeClipboardFilesAdvertisement,
                        clipboard_files = wireAds
                    });
                }
            });
        }
        else if (!string.IsNullOrEmpty(text))
        {
            SendJson(new
            {
                type = AcessoRemotoProtocol.TypeClipboardText,
                text = text
            });
        }
    }

    private Stream? OpenPullStreamFromHost(IReadOnlyList<AcessoRemotoClipboardFileItem> filesAd, int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= filesAd.Count)
            return null;

        var item = filesAd[fileIndex];
        if (item.IsDir)
            return null;

        string transferId = Guid.NewGuid().ToString("N");
        var pullStream = new RemoteClipboardPullStream(item.Size);
        _viewerClipboardPullStreams[transferId] = pullStream;

        _ = Task.Run(async () =>
        {
            await SendJsonAsync(new
            {
                type = AcessoRemotoProtocol.TypeClipboardFileRequest,
                file_index = fileIndex,
                transfer_id = transferId
            });
        });

        return pullStream;
    }

    private async Task HandleViewerClipboardFileRequestAsync(int fileIndex, string transferId)
    {
        var item = _viewerAdvertisedFiles.FirstOrDefault(f => f.Index == fileIndex);
        if (item is null || string.IsNullOrEmpty(item.FullPath) || !System.IO.File.Exists(item.FullPath))
        {
            await SendJsonAsync(new
            {
                type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                transfer_id = transferId,
                file_index = fileIndex,
                message = "Arquivo local não encontrado na área de transferência.",
                is_last = true
            });
            return;
        }

        const int chunkSize = 64 * 1024;
        long offset = 0;

        while (_isConnected && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var (chunk, totalSize, isLast) = AcessoRemotoFileSystem.ReadFileChunk(item.FullPath, offset, chunkSize);
                await SendJsonAsync(new
                {
                    type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    transfer_id = transferId,
                    file_index = fileIndex,
                    offset = offset,
                    total_size = totalSize,
                    chunk_b64 = Convert.ToBase64String(chunk),
                    is_last = isLast
                });

                offset += chunk.Length;
                if (isLast)
                    break;
            }
            catch (Exception ex)
            {
                await SendJsonAsync(new
                {
                    type = AcessoRemotoProtocol.TypeClipboardFileChunk,
                    transfer_id = transferId,
                    file_index = fileIndex,
                    message = ex.Message,
                    is_last = true
                });
                break;
            }
        }
    }

    private void ViewScreen_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!_isConnected || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Copy;
        if (DropOverlay.Visibility != Visibility.Visible)
            DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void ViewScreen_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (DropOverlay.Visibility != Visibility.Collapsed)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ViewScreen_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!_isConnected || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
            return;

        await Task.Run(async () =>
        {
            foreach (var path in droppedPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (System.IO.File.Exists(path))
                {
                    string fileName = System.IO.Path.GetFileName(path);
                    string remoteTarget = $"DESKTOP\\{fileName}";
                    await UploadSingleFileToRemoteAsync(path, remoteTarget);
                }
                else if (System.IO.Directory.Exists(path))
                {
                    var rootInfo = new System.IO.DirectoryInfo(path);
                    string parentDir = rootInfo.Parent?.FullName ?? rootInfo.FullName;

                    await SendJsonAsync(new
                    {
                        type = "fs_mkdir",
                        path = $"DESKTOP\\{rootInfo.Name}"
                    });

                    foreach (var subDir in rootInfo.EnumerateDirectories("*", System.IO.SearchOption.AllDirectories))
                    {
                        string relDir = System.IO.Path.GetRelativePath(parentDir, subDir.FullName);
                        await SendJsonAsync(new
                        {
                            type = "fs_mkdir",
                            path = $"DESKTOP\\{relDir}"
                        });
                    }

                    foreach (var file in rootInfo.EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                    {
                        string rel = System.IO.Path.GetRelativePath(parentDir, file.FullName);
                        string remoteTarget = $"DESKTOP\\{rel}";
                        await UploadSingleFileToRemoteAsync(file.FullName, remoteTarget);
                    }
                }
            }
        });
    }

    private async Task UploadSingleFileToRemoteAsync(string localFilePath, string targetRemotePath)
    {
        try
        {
            const int chunkSize = 64 * 1024;
            using var fs = new System.IO.FileStream(localFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            long totalSize = fs.Length;
            long offset = 0;
            byte[] buffer = new byte[chunkSize];

            if (totalSize == 0)
            {
                await SendJsonAsync(new
                {
                    type = "fs_upload_chunk",
                    remote_path = targetRemotePath,
                    offset = 0L,
                    total_size = 0L,
                    chunk_b64 = Convert.ToBase64String(Array.Empty<byte>()),
                    is_last = true
                });
                return;
            }

            while (offset < totalSize && _isConnected)
            {
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                byte[] chunk = new byte[read];
                Array.Copy(buffer, chunk, read);

                bool isLast = (offset + read) >= totalSize;
                await SendJsonAsync(new
                {
                    type = "fs_upload_chunk",
                    remote_path = targetRemotePath,
                    offset = offset,
                    total_size = totalSize,
                    chunk_b64 = Convert.ToBase64String(chunk),
                    is_last = isLast
                });

                offset += read;
            }
        }
        catch (Exception ex)
        {
            AppDebugLog.Debug("AcessoRemoto", $"Falha no upload Drag&Drop de {localFilePath}: {ex.Message}");
        }
    }

    private void SendInputPacket(byte[] packet)
    {
        _ = SendInputPacketAsync(packet);
    }

    private async Task SendInputPacketAsync(byte[] packet)
    {
        var ws = _ws;
        if (!_isConnected || ws == null || ws.State != WebSocketState.Open) return;
        try
        {
            await _wsSendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isConnected && ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _wsSendLock.Release();
            }
        }
        catch { }
    }

    private void SendJson(object payload)
    {
        _ = SendJsonAsync(payload);
    }

    private async Task SendJsonAsync(object payload)
    {
        var ws = _ws;
        if (!_isConnected || ws == null || ws.State != WebSocketState.Open) return;
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _wsSendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isConnected && ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _wsSendLock.Release();
            }
        }
        catch { }
    }

    #endregion

    #region Multi-Monitor & Ações Rápidas

    // Índice 0 = todos os monitores; 1..N = monitor numerado.
    private int _selectedMonitor = 1;

    private void RebuildMonitorButtons(IReadOnlyList<string> monitorNames)
    {
        PnlMonitors.Children.Clear();
        int count = monitorNames.Count;

        if (count > 1)
            PnlMonitors.Children.Add(CreateMonitorButton(0, "Todos", "Exibir todos os monitores"));

        for (int i = 1; i <= Math.Max(1, count); i++)
        {
            var tip = i <= count ? monitorNames[i - 1] : "Monitor principal";
            PnlMonitors.Children.Add(CreateMonitorButton(i, i.ToString(), tip));
        }

        _selectedMonitor = 1;
        HighlightMonitorButtons();
    }

    private Button CreateMonitorButton(int index, string label, string tooltip)
    {
        var btn = new Button
        {
            Tag = index,
            ToolTip = tooltip,
            Height = 36,
            MinWidth = index == 0 ? 36 : 42,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand
        };

        var sp = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var monitorPath = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M 3,3 H 21 A 2,2 0 0 1 23,5 V 15 A 2,2 0 0 1 21,17 H 3 A 2,2 0 0 1 1,15 V 5 A 2,2 0 0 1 3,3 Z M 8,21 H 16 M 12,17 V 21"),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            StrokeThickness = 1.6,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            Width = 14,
            Height = 14,
            Stretch = System.Windows.Media.Stretch.Uniform,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = index == 0 ? new Thickness(0) : new Thickness(0, 0, 5, 0)
        };
        sp.Children.Add(monitorPath);

        if (index > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
        }

        btn.Content = sp;
        btn.Click += MonitorButton_Click;
        return btn;
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        _selectedMonitor = index;
        HighlightMonitorButtons();

        if (!_isConnected) return;
        int targetIdx = index == 0 ? -1 : index - 1;
        SendJson(new { type = "select_monitor", monitor_index = targetIdx });
    }

    private void HighlightMonitorButtons()
    {
        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
        var normal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        var border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
        var textNormal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));

        foreach (var child in PnlMonitors.Children.OfType<Button>())
        {
            bool selected = child.Tag is int idx && idx == _selectedMonitor;
            child.Background = selected ? accent : normal;
            child.BorderBrush = selected ? accent : border;

            if (child.Content is StackPanel sp)
            {
                foreach (var p in sp.Children.OfType<System.Windows.Shapes.Path>())
                    p.Stroke = selected ? Brushes.White : textNormal;

                foreach (var tb in sp.Children.OfType<TextBlock>())
                    tb.Foreground = selected ? Brushes.White : textNormal;
            }
        }
    }
    private void BtnBlockInput_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected) return;
        SendJson(new { type = "block_input", blocked = !_isInputBlocked });
    }

    private void MenuAction_CAD_Click(object sender, RoutedEventArgs e) => SendJson(new { type = "quick_action", action = "cad" });
    private void MenuAction_Lock_Click(object sender, RoutedEventArgs e) => SendJson(new { type = "quick_action", action = "lock" });
    private void MenuAction_Taskmgr_Click(object sender, RoutedEventArgs e) => SendJson(new { type = "quick_action", action = "taskmgr" });
    private void MenuAction_Desktop_Click(object sender, RoutedEventArgs e) => SendJson(new { type = "quick_action", action = "desktop" });
    private void MenuAction_Cmd_Click(object sender, RoutedEventArgs e) => SendJson(new { type = "quick_action", action = "cmd" });

    private void MenuAction_Reboot_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Deseja realmente reiniciar o computador remoto em 10 segundos?", "Confirmar Reinicialização", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
            SendJson(new { type = "quick_action", action = "reboot" });
    }

    private void BtnFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousStyle = WindowStyle;
            _previousState = WindowState;
            _previousResizeMode = ResizeMode;

            // Para cobrir 100% da tela e ocultar a barra de tarefas do Windows:
            // 1. Redefine temporariamente para Normal se já estiver Maximized (força recálculo no WPF)
            // 2. WindowStyle.None + ResizeMode.NoResize instrui o WPF e o DWM a cobrir todo o monitor físico
            // 3. Topmost = true garante sobreposição total à barra de tarefas
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;
            Activate();

            _isFullscreen = true;
            PathFullscreen.Data = System.Windows.Media.Geometry.Parse("M 4,14 H 10 V 20 M 20,10 H 14 V 4 M 14,10 L 21,3 M 3,21 L 10,14");
            BtnFullscreen.ToolTip = "Sair da tela cheia (Esc)";
        }
        else
        {
            Topmost = false;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousState;

            _isFullscreen = false;
            PathFullscreen.Data = System.Windows.Media.Geometry.Parse("M 15,3 H 21 V 9 M 9,21 H 3 V 15 M 21,3 L 14,10 M 3,21 L 10,14");
            BtnFullscreen.ToolTip = "Alternar tela cheia";
        }
    }

    #endregion

    #region Transferência de Arquivos

    private void TxtCurrentPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SendJson(new { type = "fs_list", path = TxtCurrentPath.Text.Trim() });
    }

    private void BtnRefreshFiles_Click(object sender, RoutedEventArgs e)
    {
        SendJson(new { type = "fs_list", path = _currentRemotePath });
    }

    private void BtnUpDir_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentRemotePath.TrimEnd('\\'))?.FullName;
        if (!string.IsNullOrEmpty(parent))
            SendJson(new { type = "fs_list", path = parent });
        else
            SendJson(new { type = "fs_list", path = "DRIVES" });
    }

    private void LvFiles_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(LvFiles, e.GetPosition(LvFiles));
        if (hit != null)
        {
            var depObj = hit.VisualHit;
            while (depObj != null && depObj != LvFiles)
            {
                if (depObj is System.Windows.Controls.ListViewItem lvi)
                {
                    lvi.IsSelected = true;
                    lvi.Focus();
                    break;
                }
                depObj = VisualTreeHelper.GetParent(depObj);
            }
        }
    }

    private void LvFiles_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = LvFiles.SelectedItem as RemoteViewerFileItem;
        if (item == null)
        {
            MenuOpenFolder.Visibility = Visibility.Collapsed;
            MenuDownloadItem.IsEnabled = false;
            MenuDownloadItem.Header = "Baixar selecionado";
            MenuDeleteItem.IsEnabled = false;
            MenuDeleteItem.Header = "Excluir selecionado";
        }
        else
        {
            MenuOpenFolder.Visibility = item.IsDir ? Visibility.Visible : Visibility.Collapsed;
            MenuDownloadItem.IsEnabled = true;
            MenuDownloadItem.Header = item.IsDir ? "Baixar pasta como .zip" : "Baixar arquivo";
            MenuDeleteItem.IsEnabled = true;
            MenuDeleteItem.Header = item.IsDir ? "Excluir pasta..." : "Excluir arquivo...";
        }
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LvFiles.SelectedItem is RemoteViewerFileItem item && item.IsDir)
        {
            string newPath = System.IO.Path.Combine(_currentRemotePath, item.Name);
            SendJson(new { type = "fs_list", path = newPath });
        }
    }

    private void LvFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvFiles.SelectedItem is RemoteViewerFileItem item)
        {
            if (item.IsDir)
            {
                string newPath = Path.Combine(_currentRemotePath, item.Name);
                SendJson(new { type = "fs_list", path = newPath });
            }
            else
            {
                BtnDownloadFile_Click(sender, e);
            }
        }
    }

    private void BtnDownloadFile_Click(object sender, RoutedEventArgs e)
    {
        if (LvFiles.SelectedItem is not RemoteViewerFileItem item)
        {
            MessageBox.Show("Por favor, selecione um arquivo ou pasta para baixar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string defaultFileName = item.IsDir ? $"{item.Name}.zip" : item.Name;
        string title = item.IsDir ? "Salvar Pasta Remota Compactada (.zip)" : "Salvar Arquivo Remoto no Computador Local";
        string filter = item.IsDir ? "Arquivo ZIP (*.zip)|*.zip|Todos os arquivos (*.*)|*.*" : "Todos os arquivos (*.*)|*.*";

        var sfd = new SaveFileDialog
        {
            FileName = defaultFileName,
            Title = title,
            Filter = filter
        };

        if (sfd.ShowDialog() == true)
        {
            _downloadDestPath = sfd.FileName;
            _downloadStream = System.IO.File.Create(_downloadDestPath);
            TransferProgressBar.Value = 0;
            LblTransferStatus.Text = item.IsDir ? $"Compactando e baixando pasta '{item.Name}'..." : $"Iniciando download de {item.Name}...";

            string remoteFilePath = Path.Combine(_currentRemotePath, item.Name);
            SendJson(new
            {
                type = "fs_download_start",
                remote_path = remoteFilePath,
                transfer_id = Guid.NewGuid().ToString()
            });
        }
    }

    private async void BtnUploadFile_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecione o Arquivo Local para Enviar ao Cliente"
        };

        if (ofd.ShowDialog() == true)
        {
            var localPath = ofd.FileName;
            var fileName = Path.GetFileName(localPath);
            var targetRemotePath = Path.Combine(_currentRemotePath, fileName);

            TransferProgressBar.Value = 0;
            LblTransferStatus.Text = $"Iniciando envio de {fileName}...";

            await Task.Run(async () =>
            {
                const int chunkSize = 64 * 1024;
                using var fs = new System.IO.FileStream(localPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                long totalSize = fs.Length;
                long offset = 0;
                byte[] buffer = new byte[chunkSize];

                if (totalSize == 0)
                {
                    await SendJsonAsync(new
                    {
                        type = "fs_upload_chunk",
                        remote_path = targetRemotePath,
                        offset = 0L,
                        total_size = 0L,
                        chunk_b64 = "",
                        is_last = true
                    });
                }

                while (offset < totalSize && _isConnected)
                {
                    int read = fs.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    byte[] chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);

                    bool isLast = (offset + read) >= totalSize;
                    string b64 = Convert.ToBase64String(chunk);

                    await SendJsonAsync(new
                    {
                        type = "fs_upload_chunk",
                        remote_path = targetRemotePath,
                        offset = offset,
                        total_size = totalSize,
                        chunk_b64 = b64,
                        is_last = isLast
                    });

                    offset += read;
                    int percent = (int)(offset * 100 / totalSize);

                    Dispatcher.Invoke(() =>
                    {
                        TransferProgressBar.Value = percent;
                        LblTransferStatus.Text = $"Enviando... {percent}% ({offset / 1024} KB de {totalSize / 1024} KB)";
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    TransferProgressBar.Value = 100;
                    LblTransferStatus.Text = "Arquivo enviado com sucesso!";
                    MessageBox.Show($"Arquivo enviado com sucesso para o cliente:\n{targetRemotePath}", "Envio Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                    SendJson(new { type = "fs_list", path = _currentRemotePath });
                });
            });
        }
    }

    private void BtnNewFolder_Click(object sender, RoutedEventArgs e)
    {
        LblNewFolderLocation.Text = $"Local: {_currentRemotePath}";
        TxtNewFolderName.Text = "Nova Pasta";
        ModalNewFolderOverlay.Visibility = Visibility.Visible;
        TxtNewFolderName.Focus();
        TxtNewFolderName.SelectAll();
    }

    private void BtnConfirmNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var folderName = TxtNewFolderName.Text.Trim();
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            string targetDir = Path.Combine(_currentRemotePath, folderName);
            SendJson(new { type = "fs_mkdir", path = targetDir });
        }
        ModalNewFolderOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnCancelNewFolder_Click(object sender, RoutedEventArgs e)
    {
        ModalNewFolderOverlay.Visibility = Visibility.Collapsed;
    }

    private void ModalNewFolder_Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ModalNewFolderOverlay.Visibility = Visibility.Collapsed;
    }

    private void TxtNewFolderName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnConfirmNewFolder_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            BtnCancelNewFolder_Click(sender, e);
        }
    }

    private void BtnDeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (LvFiles.SelectedItem is not RemoteViewerFileItem item)
        {
            MessageBox.Show("Selecione um arquivo ou pasta para excluir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = MessageBox.Show($"Deseja realmente excluir '{item.Name}' do computador remoto?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
        {
            string targetPath = Path.Combine(_currentRemotePath, item.Name);
            SendJson(new { type = "fs_delete", path = targetPath });
        }
    }

    #endregion

    #region Chat & Logs

    private void TxtChatMessage_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnSendChat_Click(sender, e);
    }

    private void BtnSendChat_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtChatMessage.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var time = DateTime.Now.ToString("HH:mm:ss");
        var (effectiveName, _) = GetEffectiveTechnicianInfo();

        _chatItems.Add(new RemoteViewerChatItem
        {
            Sender = $"{effectiveName} (Você)",
            Time = time,
            Text = text,
            BgColor = "#EEF2FF",
            SenderColor = "#4F46E5"
        });

        SendJson(new
        {
            type = "chat_message",
            sender = effectiveName,
            text = text,
            time = time
        });

        TxtChatMessage.Text = "";
        if (_chatItems.Count > 0)
            LstChat.ScrollIntoView(_chatItems[_chatItems.Count - 1]);
    }

    private void BtnExportChat_Click(object sender, RoutedEventArgs e)
    {
        if (_chatItems.Count == 0)
        {
            MessageBox.Show("O histórico de chat está vazio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sfd = new SaveFileDialog
        {
            FileName = $"Chat_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Arquivo de Texto (*.txt)|*.txt",
            Title = "Salvar Histórico de Chat"
        };

        if (sfd.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine($" HISTÓRICO DE SUPORTE REMOTO - {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine("==================================================");
            sb.AppendLine();

            foreach (var msg in _chatItems)
                sb.AppendLine($"[{msg.Time}] {msg.Sender}: {msg.Text}");

            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("Histórico exportado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion
}
