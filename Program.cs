﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace  workflow_input_output_variations_autocad.io
{
    class Credentials
    {
        //get your ConsumerKey/ConsumerSecret at http://developer.autodesk.com
        public static string ConsumerKey = "";
        public static string ConsumerSecret = "";
    }
    class Program
    {
        static string GetToken()
        {
            using (var client = new HttpClient())
            {
                var values = new List<KeyValuePair<string, string>>();
                values.Add(new KeyValuePair<string, string>("client_id", Credentials.ConsumerKey));
                values.Add(new KeyValuePair<string, string>("client_secret", Credentials.ConsumerSecret));
                values.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
                var requestContent = new FormUrlEncodedContent(values);
                var response = client.PostAsync("https://developer.api.autodesk.com/authentication/v1/authenticate", requestContent).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                var resValues = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                return resValues["token_type"] + " " + resValues["access_token"];
            }
        }
        static void DownloadToDocs(string url, string localFile)
        {
            if (url == null)
                return;
            var client = new HttpClient();
            var content = (StreamContent)client.GetAsync(url).Result.Content;
            var fname = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), localFile);
            Console.WriteLine("Downloading to {0}.", fname);
            using (var output = System.IO.File.Create(fname))
            {
                content.ReadAsStreamAsync().Result.CopyTo(output);
                output.Close();
            }
        }
        static void SubmitWorkitem(string inResource, string inResourceKind, string outResource, string outResourceKind)
        {
            //create a workitem
            var wi = new AIO.WorkItem()
            {
                UserId = "", //must be set to empty
                Id = "", //must be set to empty
                Arguments = new AIO.Arguments(),
                Version = 1, //should always be 1
                ActivityId = new AIO.EntityId() { UserId = "Shared", Id = "PlotToPDF" } //PlotToPDF is a predefined activity
            };

            wi.Arguments.InputArguments.Add(new AIO.Argument()
            {
                Name = "HostDwg",// Must match the input parameter in activity
                Resource = inResource,
                ResourceKind = inResourceKind,
                StorageProvider = "Generic" //Generic HTTP download (as opposed to A360)
            });
            wi.Arguments.OutputArguments.Add(new AIO.Argument()
            {
                Name = "Result", //must match the output parameter in activity
                StorageProvider = "Generic", //Generic HTTP upload (as opposed to A360)
                HttpVerb = "POST", //use HTTP POST when delivering result
                Resource = null //use storage provided by AutoCAD.IO
            });

            container.AddToWorkItems(wi);
            Console.WriteLine("Submitting workitem...");
            container.SaveChanges();

            //polling loop
            do
            {
                Console.WriteLine("Sleeping for 2 sec...");
                System.Threading.Thread.Sleep(2000);
                container.LoadProperty(wi, "Status"); //http request is made here
                Console.WriteLine("WorkItem status: {0}", wi.Status);
            }
            while (wi.Status == "Pending" || wi.Status == "InProgress");

            //re-query the service so that we can look at the details provided by the service
            container.MergeOption = System.Data.Services.Client.MergeOption.OverwriteChanges;
            wi = container.WorkItems.Where(p => p.UserId == wi.UserId && p.Id == wi.Id).First();

            //Resource property of the output argument "Result" will have the output url
            var url = wi.Arguments.OutputArguments.First(a => a.Name == "Result").Resource;
            DownloadToDocs(url, "AIO.pdf");

            //download the status report
            url = wi.StatusDetails.Report;
            DownloadToDocs(url, "AIO-report.txt");

        }
        static AIO.Container container = null;
        static void Main(string[] args)
        {
            //instruct client side library to insert token as Authorization value into each request
            container = new AIO.Container(new Uri("https://developer.api.autodesk.com/autocad.io/v1/"));
            container.SendingRequest2 += (sender, e) => e.RequestMessage.SetHeader("Authorization", GetToken());

            //single file without xrefs
            SubmitWorkitem("https://github.com/Developer-Autodesk/library-sample-autocad.io/blob/master/A-01.dwg?raw=true", "Simple", null, "Simple");

            //file with xrefs using inline json syntax
            dynamic files = new ExpandoObject();
            files.Resource = "https://github.com/Developer-Autodesk/library-sample-autocad.io/blob/master/A-01.dwg?raw=true";
            files.LocalFileName = "A-01.dwg";
            files.RelatedFiles = new ExpandoObject[2];
            files.RelatedFiles[0] = new ExpandoObject();
            files.RelatedFiles[0].Resource = "https://github.com/Developer-Autodesk/library-sample-autocad.io/blob/master/Res/Grid%20Plan.dwg?raw=true";
            files.RelatedFiles[0].LocalFileName = "Grid Plan.dwg";
            files.RelatedFiles[1] = new ExpandoObject();
            files.RelatedFiles[1].Resource = "https://github.com/Developer-Autodesk/library-sample-autocad.io/blob/master/Res/Wall%20Base.dwg?raw=true";
            files.RelatedFiles[1].LocalFileName = "Wall Base.dwg";
            var json = JsonConvert.SerializeObject(files);
            SubmitWorkitem(json,"RemoteFileResource", null, "Simple");

            //etransmit pacakge
            SubmitWorkitem("https://github.com/Developer-Autodesk/library-sample-autocad.io/blob/master/A-01.zip?raw=true", "EtransmitPackage", null, "Simple");
        }
    }
}