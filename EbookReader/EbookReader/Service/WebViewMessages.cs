﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EbookReader.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xam.Plugin.Abstractions;

namespace EbookReader.Service {
    public class WebViewMessages : IWebViewMessages {

        FormsWebView _webView;
        bool webViewLoaded = false;

        public event EventHandler<Model.WebViewMessages.PageChange> OnPageChange;
        public event EventHandler<Model.WebViewMessages.NextChapterRequest> OnNextChapterRequest;
        public event EventHandler<Model.WebViewMessages.PrevChapterRequest> OnPrevChapterRequest;
        public event EventHandler<Model.WebViewMessages.OpenQuickPanelRequest> OnOpenQuickPanelRequest;

        public WebViewMessages(FormsWebView webView) {
            _webView = webView;

            webView.RegisterGlobalCallback("csCallback", (data) => {
                this.Parse(data);
            });

            webView.OnContentLoaded += WebView_OnContentLoaded;
        }

        public void Send(string action, object data) {

            if (this.webViewLoaded) {
                var json = JsonConvert.SerializeObject(new {
                    Action = action,
                    Data = data,
                });

                var toSend = Base64Helper.Encode(json);
                _webView.InjectJavascript(string.Format("Messages.parse('{0}')", toSend));
            }

        }

        public void Parse(string data) {
            var json = JsonConvert.DeserializeObject<Model.WebViewMessages.Message>(Base64Helper.Decode(data));

            var messageType = Type.GetType(string.Format("EbookReader.Model.WebViewMessages.{0}", json.Action));
            var msg = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(json.Data), messageType);

            switch (json.Action) {
                case Model.WebViewMessages.PageChange.Name:
                    this.OnPageChange?.Invoke(this, msg as Model.WebViewMessages.PageChange);
                    break;
                case Model.WebViewMessages.NextChapterRequest.Name:
                    this.OnNextChapterRequest?.Invoke(this, msg as Model.WebViewMessages.NextChapterRequest);
                    break;
                case Model.WebViewMessages.PrevChapterRequest.Name:
                    this.OnPrevChapterRequest?.Invoke(this, msg as Model.WebViewMessages.PrevChapterRequest);
                    break;
                case Model.WebViewMessages.OpenQuickPanelRequest.Name:
                    this.OnOpenQuickPanelRequest?.Invoke(this, msg as Model.WebViewMessages.OpenQuickPanelRequest);
                    break;
            }

        }

        private void WebView_OnContentLoaded(Xam.Plugin.Abstractions.Events.Inbound.ContentLoadedDelegate eventObj) {
            this.webViewLoaded = true;
        }
    }
}
