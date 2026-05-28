using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Janela modal com os termos de uso da abertura de chamados.</summary>
internal sealed class TermosDeUsoChamadosForm : Form
{
    internal TermosDeUsoChamadosForm()
    {
        Text = "Termos de uso";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 520);
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var body = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = ShellTheme.TextPrimary,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0, 0, 0, 12),
            TabStop = false,
        };
        body.Text = TermosDeUsoChamadosTexto.Corpo;

        var btnFechar = new Button
        {
            Text = "Fechar",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(20, 0, 20, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            BackColor = ShellTheme.Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
        };
        btnFechar.FlatAppearance.BorderSize = 0;
        btnFechar.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 82, 221);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false,
            BackColor = ShellTheme.MainBg,
        };
        bottom.Controls.Add(btnFechar);

        AcceptButton = btnFechar;
        CancelButton = btnFechar;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20, 16, 20, 12),
            BackColor = ShellTheme.MainBg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(bottom, 0, 1);

        Controls.Add(root);
    }
}

internal static class TermosDeUsoChamadosTexto
{
    internal const string Corpo =
"""
# TERMOS DE USO — ABERTURA DE CHAMADOS TÉCNICOS

## 1. Finalidade do Sistema de Chamados

O sistema de abertura de chamados da Sistec tem como finalidade registrar solicitações de suporte técnico, manutenção, dúvidas operacionais e demais demandas relacionadas aos serviços prestados pela área de Tecnologia da Informação.

Ao utilizar este sistema, o usuário declara estar ciente de que o formulário destina-se exclusivamente ao registro de solicitações legítimas, relacionadas às atividades profissionais e aos recursos disponibilizados pela empresa.

## 2. Uso Adequado do Sistema

O usuário compromete-se a utilizar o sistema de abertura de chamados de forma responsável, ética e em conformidade com as diretrizes internas da empresa, mantendo boa conduta e respeito durante todo o processo de atendimento.

Não é permitido:

* Utilizar o sistema para fins pessoais ou indevidos;
* Registrar solicitações com conteúdo ofensivo, inadequado ou fora do escopo de atendimento técnico;
* Fornecer informações falsas, incompletas ou intencionalmente incoerentes;
* Abrir chamados que não estejam relacionados às atividades profissionais ou aos serviços prestados pela Sistec.

## 3. Responsabilidade pelas Informações Fornecidas

Ao realizar a abertura de um chamado, o usuário declara que todas as informações fornecidas são verdadeiras, claras e suficientes para o entendimento da solicitação.

Caso as informações apresentadas não estejam claras, completas ou coerentes, a Sistec poderá solicitar esclarecimentos adicionais antes de dar continuidade ao atendimento.

Chamados que contenham informações insuficientes ou inconsistentes poderão ter seu atendimento suspenso até que os dados necessários sejam fornecidos.

## 4. Comunicação e Contato com o Usuário

Ao abrir um chamado, o usuário autoriza expressamente a Sistec a entrar em contato por meio do telefone informado no formulário, incluindo telefones de uso pessoal, quando necessário para esclarecimentos, acompanhamento ou finalização da solicitação.

O usuário declara estar ciente de que:

* O telefone informado deverá estar correto e disponível para contato;
* A Sistec poderá realizar tentativas de contato para tratar assuntos relacionados ao chamado;
* O contato poderá ocorrer durante o horário comercial ou conforme necessidade operacional.

Na impossibilidade de contato devido à ausência de resposta, indisponibilidade do usuário ou fornecimento de telefone incorreto ou inválido, o chamado poderá ser encerrado sem aviso prévio.

## 5. Encerramento de Chamados

A Sistec reserva-se o direito de encerrar chamados nas seguintes situações:

* Falta de resposta do usuário após tentativas de contato;
* Fornecimento de informações insuficientes ou incoerentes;
* Solicitações fora do escopo de atendimento;
* Uso inadequado do sistema de chamados;
* Resolução da solicitação ou conclusão do atendimento técnico.

O encerramento do chamado poderá ocorrer sem aviso prévio nos casos em que não seja possível dar continuidade ao atendimento.

## 6. Uso de Telefone Pessoal para Contato

Ao informar um número de telefone no momento da abertura do chamado, o usuário declara estar ciente e de acordo que este poderá ser utilizado pela Sistec exclusivamente para fins relacionados ao atendimento técnico e comunicação operacional.

O usuário reconhece que o fornecimento voluntário do número de telefone caracteriza autorização para contato, não cabendo responsabilização futura à Sistec pelo uso desse meio de comunicação para tratativas relacionadas ao suporte técnico.

## 7. Aceite dos Termos

Ao utilizar o sistema de abertura de chamados, o usuário declara que leu, compreendeu e concorda integralmente com os presentes Termos de Uso.

O aceite destes termos é obrigatório para a utilização do sistema e registro de solicitações.

## 8. Atualização dos Termos

A Sistec reserva-se o direito de atualizar estes Termos de Uso a qualquer momento, sempre que necessário, visando adequação às normas internas, procedimentos operacionais ou requisitos legais.

As versões atualizadas passarão a vigorar a partir de sua publicação no sistema.
""";
}
