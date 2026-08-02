// Contract Language Support — VS Code client.
//
// Starts the Contract language server via the CLI tool (`ccl lsp`), which is
// installed as a global dotnet tool (install.ps1 → `dotnet tool install -g cclc`)
// and therefore on PATH. Override with the contract.languageServer.path setting.

'use strict';

const vscode = require('vscode');
const {
  LanguageClient,
  TransportKind,
  RevealOutputChannelOn,
} = require('vscode-languageclient/node');

/** @type {LanguageClient | undefined} */
let client;

function activate(context) {
  const clientOptions = {
    documentSelector: [
      { language: 'contract', scheme: 'file' },
      { language: 'contract', scheme: 'untitled' },
    ],
    synchronize: {
      configurationSection: 'contract',
    },
    outputChannelName: 'Contract Language Server',
    revealOutputChannelOn: RevealOutputChannelOn.Never,
  };

  client = new LanguageClient(
    'contract',
    'Contract Language Server',
    makeServerOptions(),
    clientOptions
  );

  context.subscriptions.push(client.start());
}

function makeServerOptions() {
  const configured = vscode.workspace
    .getConfiguration('contract')
    .get('languageServer.path', '');
  const command = configured || 'ccl';

  return {
    run: { command, args: ['lsp'], transport: TransportKind.stdio },
    debug: { command, args: ['lsp', '--trace'], transport: TransportKind.stdio },
  };
}

function deactivate() {
  if (!client) return undefined;
  return client.stop();
}

module.exports = { activate, deactivate };
