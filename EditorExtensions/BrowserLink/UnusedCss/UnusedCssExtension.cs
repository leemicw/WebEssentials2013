﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.VisualStudio.Web.BrowserLink;

namespace MadsKristensen.EditorExtensions.BrowserLink.UnusedCss
{
    public class UnusedCssExtension : BrowserLinkExtension, IBrowserLinkActionProvider
    {
        private static readonly ConcurrentDictionary<BrowserLinkConnection, UnusedCssExtension> ExtensionByConnection = new ConcurrentDictionary<BrowserLinkConnection, UnusedCssExtension>();
        private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _validSheetUrlsForPage = new ConcurrentDictionary<string, ConcurrentBag<string>>();
        private readonly UploadHelper _uploadHelper;
        private readonly BrowserLinkConnection _connection;
        private string _currentLocation;
        private bool _isRecording;
        private bool _isAggregatingRecordingData;
        private bool _isRunningShapshot;

        static UnusedCssExtension()
        {
            IgnoreList = new List<string>();
        }

        internal static void All(Action<UnusedCssExtension> method)
        {
            MessageDisplayManager.DisplaySource = MessageDisplaySource.Project;
            foreach (var extension in ExtensionByConnection.Values)
            {
                method(extension);
            }
        }

        public BrowserLinkConnection Connection { get { return _connection; } }

        public UnusedCssExtension(BrowserLinkConnection connection)
        {
            _uploadHelper = new UploadHelper();
            _connection = connection;
        }

        public override void OnDisconnecting(BrowserLinkConnection connection)
        {
            UnusedCssExtension extension;
            ExtensionByConnection.TryRemove(connection, out extension);
        }

        private void SetRecordingButtonDisplayProperties(BrowserLinkAction obj)
        {
            obj.Enabled = !_isAggregatingRecordingData;
            obj.ButtonText = _isRecording ? "Stop Recording" : "Start Recording";
        }

        [BrowserLinkCallback] // This method can be called from JavaScript
        public void FinishedRecording(string expectLocation, Guid operationId, string chunkContents, int chunkNumber, int chunkCount)
        {
            if (_currentLocation != expectLocation)
            {
                return;
            }

            SessionResult result;
            if (_uploadHelper.TryFinishOperation(operationId, chunkContents, chunkNumber, chunkCount, out result))
            {
                UsageRegistry.Merge(_connection, result);
                MessageDisplayManager.ShowWarningsFor(_connection, result);
            }
        }

        [BrowserLinkCallback]
        public void FinishedSnapshot(string expectLocation, Guid operationId, string chunkContents, int chunkNumber, int chunkCount)
        {
            if (_currentLocation != expectLocation)
            {
                return;
            }

            SessionResult result;
            if (_uploadHelper.TryFinishOperation(operationId, chunkContents, chunkNumber, chunkCount, out result))
            {
                UsageRegistry.Merge(_connection, result);
                MessageDisplayManager.ShowWarningsFor(_connection, result);
            }
        }

        [BrowserLinkCallback]
        public void PageLoaded(string currentLocation)
        {
            _currentLocation = currentLocation;
            _uploadHelper.Reset();
            ResetCollectionStatuses();
        }

        private void ResetCollectionStatuses()
        {
            _isAggregatingRecordingData = false;
            _isRecording = false;
            _isRunningShapshot = false;
        }

        public IEnumerable<BrowserLinkAction> Actions
        {
            get
            {
                yield return new BrowserLinkAction("Snapshot Page", SnapshotPage, SetSnapshotButtonDisplayProperties);
                yield return new BrowserLinkAction("Start Recording", ToggleRecordingMode, SetRecordingButtonDisplayProperties);
            }
        }

        private void ToggleRecordingMode()
        {
            if (_isRecording)
            {
                Clients.Call(_connection, "stopRecording", Guid.NewGuid());
            }
            else
            {
                Clients.Call(_connection, "startRecording");
            }

            _isRecording = !_isRecording;
        }

        public void SnapshotPage()
        {
            Clients.Call(_connection, "snapshotPage", Guid.NewGuid());
        }

        public IEnumerable<string> GetValidSheetUrlsForCurrentLocation()
        {
            var location = _connection.Url.ToString().ToLowerInvariant();
            ConcurrentBag<string> result;
            
            if (!_validSheetUrlsForPage.TryGetValue(location, out result))
            {
                return new string[0];
            }

            return result;
        }

        private void SetSnapshotButtonDisplayProperties(BrowserLinkAction obj)
        {
            obj.Enabled = !_isRunningShapshot;
            obj.ButtonText = _isRunningShapshot ? "Snapshot in progress..." : "Snapshot Page";
        }

        [BrowserLinkCallback]
        public void ParseSheets(string expectLocation, Guid operationId, string chunkContents, int chunkNumber, int chunkCount)
        {
            if (_currentLocation != expectLocation)
            {
                return;
            }

            List<string> result;
            if (_uploadHelper.TryFinishOperation(operationId, chunkContents, chunkNumber, chunkCount, out result))
            {
                _validSheetUrlsForPage.AddOrUpdate(_connection.Url.ToString().ToLowerInvariant(), u => new ConcurrentBag<string>(result), (u, x) => new ConcurrentBag<string>(result));
            }
        }

        public static List<string> IgnoreList { get; private set; }

        [BrowserLinkCallback]
        public void GetIgnoreList()
        {
            Clients.Call(_connection, "getLinkedStyleSheetUrls", IgnoreList);
        }
    }
}