﻿using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using Certes.Cli.Options;
using NLog;
using ValidationFunc = System.Func<Certes.Cli.Options.AccountOptions, bool>;

namespace Certes.Cli.Processors
{
    internal class AccountCommand
    {
        private static readonly List<(AccountAction Action, ValidationFunc IsValid, string Help)> validations = new List<(AccountAction, ValidationFunc, string)>
        {
            (AccountAction.New, (ValidationFunc)(o => !string.IsNullOrWhiteSpace(o.Email)), "Please enter the admin email."),
            (AccountAction.Update, (ValidationFunc)(o => !string.IsNullOrWhiteSpace(o.Email) || o.AgreeTos), "Please enter the data to update."),
            (AccountAction.Set, (ValidationFunc)(o => !string.IsNullOrWhiteSpace(o.Path)), "Please enter the key file path."),
        };

        public ILogger Logger { get; } = LogManager.GetCurrentClassLogger();
        private AccountOptions Args { get; }

        public AccountCommand(AccountOptions args)
        {
            Args = args;
        }

        public static AccountOptions TryParse(ArgumentSyntax syntax)
        {
            var options = new AccountOptions();

            var command = Command.Undefined;
            syntax.DefineCommand("account", ref command, Command.Account, "Manange ACME account.");
            if (command == Command.Undefined)
            {
                return null;
            }

            syntax.DefineOption("email", ref options.Email, "Email used for registration and recovery contact. (default: None)");
            syntax.DefineOption("agree-tos", ref options.AgreeTos, $"Agree to the ACME Subscriber Agreement. (default: {options.AgreeTos})");

            syntax.DefineOption("server", ref options.Server, s => new Uri(s), $"ACME Directory Resource URI.");
            syntax.DefineOption("key", ref options.Path, $"File path to the account key to use.");
            syntax.DefineOption("verbose", ref options.Verbose, $"Print process log.");

            syntax.DefineParameter(
                "action",
                ref options.Action,
                a => (AccountAction)Enum.Parse(typeof(AccountAction), a?.Replace("-", ""), true),
                "Account action");

            foreach (var validation in validations)
            {
                if (options.Action == validation.Action && !validation.IsValid(options))
                {
                    syntax.ReportError(validation.Help);
                }
            }

            return options;
        }

        public async Task<object> Process()
        {
            switch (Args.Action)
            {
                case AccountAction.Info:
                    return await LoadAccountInfo();
                case AccountAction.New:
                    return await NewAccount();
            }

            throw new NotSupportedException();
        }

        private async Task<object> NewAccount()
        {
            var key = await Args.LoadKey();
            if (key != null && !Args.Force)
            {
                throw new Exception("An account key already exists, use '--force' option to overwrite the existing key.");
            }

            Logger.Debug("Using ACME server {0}.", Args.Server);
            var ctx = ContextFactory.Create(Args.Server, null);

            Logger.Debug("Creating new account, email='{0}', agree='{1}'", Args.Email, Args.AgreeTos);
            var acctCtx = await ctx.NewAccount(Args.Email, Args.AgreeTos);
            Logger.Debug("Created new account at {0}", acctCtx.Location);

            var path = Args.GetKeyPath();
            await FileUtil.WriteAllTexts(path, ctx.AccountKey.ToPem());

            return await acctCtx.Resource();
        }

        private async Task<object> LoadAccountInfo()
        {
            var key = await Args.LoadKey();
            if (key == null)
            {
                throw new Exception("No account key is available.");
            }

            Logger.Debug("Using ACME server {0}.", Args.Server);
            var ctx = ContextFactory.Create(Args.Server, key);
            var acctCtx = await ctx.Account();

            Logger.Debug("Retrieve account at {0}", acctCtx.Location);
            return await acctCtx.Resource();
        }
    }
}
