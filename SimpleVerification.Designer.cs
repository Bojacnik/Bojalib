using System;
using System.Windows.Forms;

namespace Bojalib
{
    namespace Security
    {
        namespace Verification
        {
            public partial class SimpleVerification
            {
                /// <summary>
                /// Required designer variable.
                /// </summary>
                private System.ComponentModel.IContainer components = null;

                /// <summary>
                /// Clean up any resources being used.
                /// </summary>
                /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
                protected override void Dispose(bool disposing)
                {
                    if (disposing && (components != null))
                    {
                        components.Dispose();
                    }
                    base.Dispose(disposing);
                }

                #region Windows Form Designer generated code

                /// <summary>
                /// Required method for Designer support - do not modify
                /// the contents of this method with the code editor.
                /// </summary>
                private void InitializeComponent()
                {
                    this.btnVerify = new System.Windows.Forms.Button();
                    this.tbPassw = new System.Windows.Forms.TextBox();
                    this.lblPassw = new System.Windows.Forms.Label();
                    this.SuspendLayout();
                    // 
                    // btnVerify
                    // 
                    this.btnVerify.Location = new System.Drawing.Point(214, 6);
                    this.btnVerify.Name = "btnVerify";
                    this.btnVerify.Size = new System.Drawing.Size(75, 23);
                    this.btnVerify.TabIndex = 4;
                    this.btnVerify.Text = "Verify";
                    this.btnVerify.UseVisualStyleBackColor = true;
                    this.btnVerify.Click += new System.EventHandler(this.btnVerify_Click);
                    // 
                    // tbPassw
                    // 
                    this.tbPassw.Location = new System.Drawing.Point(78, 6);
                    this.tbPassw.Name = "tbPassw";
                    this.tbPassw.PasswordChar = '*';
                    this.tbPassw.Size = new System.Drawing.Size(130, 23);
                    this.tbPassw.TabIndex = 5;
                    // 
                    // lblPassw
                    // 
                    this.lblPassw.AutoSize = true;
                    this.lblPassw.Location = new System.Drawing.Point(12, 10);
                    this.lblPassw.Name = "lblPassw";
                    this.lblPassw.Size = new System.Drawing.Size(60, 15);
                    this.lblPassw.TabIndex = 6;
                    this.lblPassw.Text = "Password:";
                    // 
                    // Verification
                    // 
                    this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
                    this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                    this.ClientSize = new System.Drawing.Size(302, 37);
                    this.Controls.Add(this.lblPassw);
                    this.Controls.Add(this.tbPassw);
                    this.Controls.Add(this.btnVerify);
                    this.MaximizeBox = false;
                    this.MinimizeBox = false;
                    this.Name = "Verification";
                    this.ShowIcon = false;
                    this.Text = "Verification";
                    this.TopMost = true;
                    this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SimpleVerification_FormClosing);
                    this.ResumeLayout(false);
                    this.PerformLayout();

                }

                #endregion

                private System.Windows.Forms.Button btnVerify;
                private System.Windows.Forms.TextBox tbPassw;
                private System.Windows.Forms.Label lblPassw;
            }
        }
    }
    
}