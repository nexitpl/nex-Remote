﻿using nexRemote.Desktop.Core.Interfaces;
using nexRemote.Desktop.Win.ViewModels;
using nexRemote.Desktop.Win.Views;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace nexRemote.Desktop.Win.Services
{
    public class RemoteControlAccessServiceWin : IRemoteControlAccessService
    {
        public Task<bool> PromptForAccess(string requesterName, string organizationName)
        {
            var result = App.Current.Dispatcher.Invoke(() =>
            {
                var promptWindow = new PromptForAccessWindow();
                var viewModel = promptWindow.DataContext as PromptForAccessWindowViewModel;
                if (!string.IsNullOrWhiteSpace(requesterName))
                {
                    viewModel.RequesterName = requesterName;
                }
                if (!string.IsNullOrWhiteSpace(organizationName))
                {
                    viewModel.OrganizationName = organizationName;
                }
                promptWindow.ShowDialog();

                return viewModel.PromptResult;
            });

            return Task.FromResult(result);
        }
    }
}
