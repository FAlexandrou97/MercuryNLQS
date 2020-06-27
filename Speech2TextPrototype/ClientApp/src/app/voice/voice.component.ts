import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';
import { TData, ResponseAnswer } from './AnswerInterface'

import { VisualizationService } from '../visualization.service';


@Component({
  selector: 'app-voice-component',
  templateUrl: './voice.component.html',
  styleUrls: ['./voice.component.scss']
})
export class VoiceComponent implements OnInit{
    private resultSpeech2Text: string;
    private thinking: boolean = false;
    private listening: boolean = false;
    public voiceOutput: boolean = false;
    private responseAnswer: ResponseAnswer;
    private emptyAnswer: ResponseAnswer = {}


    private debug: boolean = false;

    constructor(private apiService: ApiService, private visualization: VisualizationService) { }

    ngOnInit() {
        this.visualization.currentAnswer.subscribe(answer => this.responseAnswer = answer);
    }

    public Speech2Text() {
        this.listening = true;
        this.apiService.getText().subscribe((res) => {
            this.resultSpeech2Text = res
            this.listening = false;
            this.getAnswer(res);
        });
    }


    public getAnswer(question: string) {
        this.visualization.onWaitForAnswer(null);
        this.thinking = true;
        this.apiService.getAnswer(question, this.voiceOutput).subscribe((res) => {
            this.visualization.onReceiveAnswer(res);
            this.thinking = false;
        });

    }

    // For testing
    public textToSpeech(text: string) {
        this.apiService.textToSpeech(text).subscribe((res) => {
        });
    }

    public onVoiceOutput(value: boolean) {
        this.voiceOutput = value;
    }
}
