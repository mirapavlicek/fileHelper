using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.VisualBasic;
using System.Security.Permissions;

namespace fileHelper
{
    [Command(Name = "File helper", Description = "App searching for files on folder and send file names with to webservice using unc folder like web service mountpoint")]
    [HelpOption("-?")]
    class Program
    {
        static Task<int> Main(string[] args) => CommandLineApplication.ExecuteAsync<Program>(args);
        private HttpClient _httpClient;

        [Argument(0, Description = "Folder for search")]
        private string folder { get; }

        [Option("--filename", Description = "Filename for send")]
        private string filename { get; }

        [Option("--recoursive", Description = "recusive search")]
        private bool recoursive { get; }

        [Option("--filetype", Description = "filetype eg. pdf, docx, xlsx, default is pdf only one type could search in once")]
        private string fileType { get; }

        [Option("--infinity", Description = "infinity looping of scanning infintiy:0 is defualt, eg. infinity:5, as number of loops")]
        private int infinity { get; }

        [Option("--delay", Description = "delay of loop,  default: 500ms eg. delay:2000 for 2s delay")]
        private int delay { get; }

        [Option("--web", Description = "weblink for webservice eg.: https://10.84.12.235/webapi/inotify/")]
        private string web { get; }

        [Option("--endpoint", Description = "endpoint for webservice eg.: NNH.Exams.RDG.Data ")]
        private string endPoint { get; }

        [Option("--ocrfile", Description = "OCR file to string")]
        private bool ocr { get; }

        [Option("--ocrbar", Description = "OCR barcode to array of strings ")]
        private string ocrBar { get; }

        [Option("--ident", Description = "Identification number od patient (RodCis or PorCis) ")]
        private string ident { get; }

        [Option("--acnumber", Description = "Accession number request ")]
        private string acnumber { get; }

        [Option("--done", Description = "Done folder - default done ")]
        private string doneFolder { get; set; }

        [Option("--error", Description = "Error folder - default error ")]
        private string errorFolder { get; set; }

        [Option("--nomoveerror", Description = "Move error files to error folder 1 - yes, 0 - no")]
        private bool noMoveError { get; set; }

        [Option("--moveExternal", Description = "Move done and error files to folder in another place (default: Search folder")]
        private bool moveExt { get; set; }

        [Option("--timeout", Description = "Timeout in ms (miliseconds) for web client (default: 5000 ms). Minimum is 100 ms maximum is 60 000 ms")]
        private int timeOut { get; set; }

        [Option("--user", Description = "Connection username")]
        private string user { get; set; }

        [Option("--pass", Description = "Connection password")]
        private string password { get; set; }

        /// <summary>
        /// Property types of ValueTuple{bool,T} translate to CommandOptionType.SingleOrNoValue.
        /// Input            | Value
        /// -----------------|--------------------------------
        /// (none)           | (false, default(TraceLevel))
        /// --trace          | (true, TraceLevel.Normal)
        /// --trace:normal   | (true, TraceLevel.Normal)
        /// --trace:verbose  | (true, TraceLevel.Verbose)
        /// </summary>
        ///

        [Option]
        public (bool HasValue, TraceLevel level) Trace { get; }

        private static bool keepRunning = true;


        private async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(folder))
            {
                app.ShowHelp();
                return 0;
            }

            if (folder.Length < 1)
            {
                Console.Error.WriteLine($"Folder not selected");
                return 1;
            }
            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine($"Folder not exist");
                return 1;
            }
         
            

            //Check Done Folder and Error folder
            if (string.IsNullOrEmpty(doneFolder))
            {
                doneFolder = "_done";
            }
            if (string.IsNullOrEmpty(errorFolder))
            {
                errorFolder = "_error";
            }

           


            if (!Directory.Exists(Path.Combine(folder,doneFolder)))
            {
                try
                {
                    LogTrace(TraceLevel.Verbose, $"Try create done folder {Path.Combine(folder, doneFolder)}");
                    Directory.CreateDirectory(Path.Combine(folder, doneFolder));
                }
                catch (Exception ex)
                {
                    LogTrace(TraceLevel.Verbose, $"Folder {Path.Combine(folder, doneFolder)} not exist / cannot be created - {ex}");
                }
            }


            if (!Directory.Exists(Path.Combine(folder, errorFolder)))
            {
                try
                {
                    LogTrace(TraceLevel.Verbose, $"Try create error folder {Path.Combine(folder, errorFolder)}");
                    Directory.CreateDirectory(Path.Combine(folder, errorFolder));
                }
                catch (Exception ex)
                {
                    LogTrace(TraceLevel.Verbose, $"Folder {Path.Combine(folder, errorFolder)} not exist / cannot be created - {ex}");
                }
            }
            LogTrace(TraceLevel.Verbose, $"Starting {folder} request with parameters {recoursive} with infinity {infinity}");



            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                keepRunning = false;
                LogTrace(TraceLevel.Verbose, $"Looping ended");

            };

            int taskDelay = 500;

            if (delay < 0)
            {
                taskDelay = 500;
            }
            else
            {
                taskDelay = delay;
            }
            Uri serviceWeb;
            String webRequestUrl;
            if (String.IsNullOrEmpty(web))
            {
                webRequestUrl = "https://10.84.12.235:57772/webapi/inotify/";
            }
            else
            {
                webRequestUrl = web;
            }
            LogTrace(TraceLevel.Verbose, $"Url: {webRequestUrl}");
            //Check web valid

            bool webResult = Uri.TryCreate(webRequestUrl, UriKind.Absolute, out serviceWeb)
    && serviceWeb.Scheme == Uri.UriSchemeHttps;

            if (webResult is false)
            {
                LogTrace(TraceLevel.Verbose, $"Service web address {web} is not valid");
                app.ShowHelp();
                return 0;
            }


            //Checkendpoit
            string _endPoint = "";
            if (String.IsNullOrEmpty(endPoint))
            {
                _endPoint = "NNH.Exams.RDG.Data";
            }
            else
            {

                _endPoint = endPoint;

            }
            LogTrace(TraceLevel.Verbose, $"EndPoint: {_endPoint}");


            if (infinity == 0)
            {
                while (keepRunning == true)
                {
                    await ProcessDirectory(folder, recoursive, fileType is null ? "pdf" : fileType, webRequestUrl, _endPoint, ident, acnumber,ocrBar);
                    await Task.Delay(taskDelay);
                }




            }
            else if (infinity > 0)
            {
                for (int i = 1; i <= infinity; i++)
                {
                    LogTrace(TraceLevel.Verbose, $"Loop {i}/{infinity}");
                    await ProcessDirectory(folder, recoursive, fileType is null ? "pdf" : fileType, webRequestUrl, _endPoint, ident, acnumber,ocrBar);
                    await Task.Delay(taskDelay);
                }
            }
            else if (String.IsNullOrEmpty(filename))
            {
                LogTrace(TraceLevel.Verbose, $"Process file {folder}/{filename}");
                await ProcessFile(folder+"/"+filename, web, _endPoint, ident, acnumber,ocrBar);
            }
            else
            {

                await ProcessDirectory(folder, recoursive, fileType is null ? "pdf" : fileType, webRequestUrl, _endPoint, ident, acnumber,ocrBar);
            }
            return 0;

        }
        public void LogTrace(TraceLevel level, string message)
        {
            if (!Trace.HasValue) return;
            if (Trace.level >= level)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{level}: {message}");
                Console.ResetColor();
            }
        }

        public async Task<int> ProcessDirectory(string targetDirectory, bool recoursion, string filetype = "pdf", string web = "https://10.84.12.235:57782/webapi/inotify/", string endpoint = "", string ident = "", string acnumber = "", string ocrBar="")
        {
            if (filetype.Length > 8 || filetype.Length < 3)
            {
                filetype = "pdf";
            }
          




            // Process the list of files found in the directory.
            string[] fileEntries = Directory.GetFiles(targetDirectory, $"*.{filetype}");
            foreach (string fileName in fileEntries)
                await ProcessFile(fileName, web, endpoint, ident, acnumber,ocrBar);



            // Recurse into subdirectories of this directory.
            if (recoursion is true)
            {
                string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
                foreach (string subdirectory in subdirectoryEntries)
                    if (!subdirectory.Contains(doneFolder) && !subdirectory.Contains(errorFolder))
                    {
                    await ProcessDirectory(subdirectory, recoursion, filetype, web, endpoint, ident, acnumber,ocrBar);
                    }
                    
            }
            return 0;

        }


        public async Task<int> ProcessFile(string path, string web, string endpoint, string ident, string acnumber,string ocrBar)
        {

            LogTrace(TraceLevel.Verbose, $"Processed file {path}");


            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);

            string fileName = Path.GetFileName(path);
            web = web.Substring(web.Length - 1) != "/" ? $"{web}/" : web;


            HttpClientHandler clientHandler = new HttpClientHandler();
            clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };


            try
            {

                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                {

                    user = "inotify";
                    password = "pWDxGjr9Df2MUWZQ";
                }

                var authenticationString = $"{user}:{password}";
                var base64EncodedAuthenticationString = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(authenticationString));


                using (_httpClient = new HttpClient(clientHandler))
                using (var formData = new MultipartFormDataContent())
                {
                    formData.Add(new StringContent(fileName), "file", "file");
                    formData.Add(new StreamContent(fs), "filebyte", "filebyte");
                    try
                    {
                        if (!string.IsNullOrEmpty(ident))
                        {
                            formData.Add(new StringContent(ident), "identifacator");
                        }
                        if (!string.IsNullOrEmpty(acnumber))
                        {
                            formData.Add(new StringContent(acnumber), "acnumber");
                        }
                        if (!string.IsNullOrEmpty(ocrBar))
                        {
                            formData.Add(new StringContent(ocrBar), "ocrbar");
                        }

                    }
                    catch (Exception)
                    {
                        LogTrace(TraceLevel.Verbose, $"No more data parset to file (accesion number, indentificator)");
                    }
                   

                    int _timeOut = 5000;

                    try {
                        _timeOut = timeOut;
                    }
                    catch (Exception)
                    {
                        _timeOut = 5000;
                    }

                    if (_timeOut < 100 || _timeOut > 60000)
                    {
                        _timeOut = 5000;
                    }    

                    _httpClient.Timeout = TimeSpan.FromMilliseconds(_timeOut);
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

                    var response = await _httpClient.PostAsync($"{web}{endpoint}", formData);
                    if (!response.IsSuccessStatusCode)
                    {
                        LogTrace(TraceLevel.Info, $"Error send fail {fileName}");
                        LogTrace(TraceLevel.Verbose, $"Error Phrase: {response.ReasonPhrase.ToString()}");
                        LogTrace(TraceLevel.Verbose, $"Error Request: {response.RequestMessage.ToString()}");
                        LogTrace(TraceLevel.Verbose, $"Error Content: {response.Content.ToString()}");

                        if (noMoveError is false)
                        {
                            //Move error files
                            try
                            {
                                if (File.Exists(Path.Combine(folder, errorFolder, fileName)))
                                    File.Delete(Path.Combine(folder, errorFolder, fileName));

                                File.Move(path, Path.Combine(folder, errorFolder, fileName));
                            }
                            catch (Exception ex)
                            {
                                LogTrace(TraceLevel.Verbose, $"Error fail move {path} - {ex}");
                            }
                        }

                        return 1;
                    }

                }
                LogTrace(TraceLevel.Verbose, $"File {path} sended!");
                //Move sended files
                try
                {
                    if (File.Exists(Path.Combine(folder, doneFolder, fileName)))
                        File.Delete(Path.Combine(folder, doneFolder, fileName));
                    
                    File.Move(path, Path.Combine(folder, doneFolder,fileName));
                }
                catch (Exception ex)
                {
                    LogTrace(TraceLevel.Verbose, $"Error fail move {path} - {ex}");
                }

            }
            catch (Exception ex)
            {
                LogTrace(TraceLevel.Verbose, $"General error in {path} - {ex}");
            }

            /* var request = new HttpRequestMessage
             {
                 RequestUri = new Uri(web),
                 Method = HttpMethod.Get

             };
         LogTrace(TraceLevel.Verbose, $"Open web application");
         request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
         var result = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
         result.Content.CopyToAsync(ms).Wait();
         LogTrace(TraceLevel.Verbose, $"Výsledek {result.IsSuccessStatusCode}");
            */
            
            return 0;




        }
    }
}

public enum TraceLevel
{
    Info = 0,
    Verbose,
}
